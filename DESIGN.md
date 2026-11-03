# Design

IPL Franchise Merchandise Store: an ASP.NET Core Web API (.NET 10) on Azure Cosmos DB (NoSQL API) with an Angular 21 front end. This document records the main decisions as four ADRs, then the API, reliability, security, observability and testing choices. Assumptions, extra scenarios, limitations and next steps are in [NOTES.md](NOTES.md).

## ADR-001: Architecture

**Context.** The hard parts of this store are business rules under concurrency: stock must never go negative, retries must not duplicate cart changes or orders, and price changes must be noticed. Those rules must be testable without a database and must not be tied to a web framework.

**Decision.** Four layers with dependencies pointing inward, plus a separate tool for the database:

| Project | Holds | Depends on |
| --- | --- | --- |
| `Domain` | Pure rules: cart limits, pricing and acceptance, checkout decision, order snapshot, stock status | nothing |
| `Application` | Use cases (catalogue, cart, checkout, recovery, orders, admin), validation, paging, and the ports (`IProductStore`, `ICheckoutStore`, ...) | Domain |
| `Infrastructure` | Cosmos DB adapters for the ports: documents, transactional batches, conditional patches, ETags | Application |
| `Api` | Controllers, problem details, JWT auth, rate limits, correlation ids, the checkout sweeper | Application, Infrastructure |
| `DbMaintenance` | Schema declaration, verification, local provisioning, data migrations, Bicep | (standalone) |

Dependency injection wires the ports to the Cosmos adapters in `Infrastructure/DependencyInjection.cs`. Unit tests replace the adapters with an in-memory fake (`UnitTests/Fakes.cs`); integration tests use the real ones against the emulator.

**Patterns used.** Ports and adapters (so rules run without Cosmos DB); the options pattern with validation at startup (a missing signing key stops the app instead of failing later); idempotency records; a commit marker plus a sweeper to finish a two-step checkout; seek (keyset) pagination with signed tokens; problem details for every error.

**Patterns avoided, and why.**

| Avoided | Why |
| --- | --- |
| MediatR / CQRS pipeline | Seven use cases do not need a dispatcher; plain services are easier to follow and debug. |
| EF Core Cosmos provider | Checkout needs transactional batches with conditional patches, ETag control and point reads; the SDK gives these directly. |
| Generic repository | Each store exposes only the operations its use cases need (e.g. `DeductAsync`), so the atomicity rules live in one place. |
| Distributed transactions / sagas across containers | Avoided by modelling: everything that must be atomic shares one partition. The one cross-container step (order record after stock deduction) is made safe with a marker and a sweeper (ADR-003). |
| Change feed processor | Not needed yet; the sweeper covers recovery. The `leases` container is deliberately not created. |

**Consequences.** Business rules are covered by fast unit tests; the Cosmos-specific code is small and tested against the emulator. The cost is some mapping code between documents, domain records and API contracts.

## ADR-002: Data model and partition keys

**Context.** Cosmos DB charges request units (RU) per operation, a logical partition holds at most 20 GB and a transactional batch must stay inside one logical partition. The access patterns are: browse and search the catalogue, read and change one shopper's cart, deduct stock for several products atomically, list one shopper's orders newest first.

**Decision.** Database `ipl-store`, declared once in [`schema.json`](backend/IplFranchise.DbMaintenance/Schema/schema.json) (read by the tool and by Bicep):

| Container | Partition key | Documents (`docKind`) | Why |
| --- | --- | --- | --- |
| `products` | `/franchiseId` | `product`, `audit` (price) | Browsing a franchise is one partition; a price change and its audit row commit in one batch. |
| `inventory` | `/tenantId` (`default`) | `stock`, `audit` (stock), `deduct`, `purchase` | One logical partition, so a checkout deducts several products, writes its marker, purchase records and audit rows in **one atomic batch**. |
| `carts` | `/userId` | `cart`, `idempotency` | Every cart operation is for one shopper; the cart change and its idempotency record commit together. Default TTL 30 days; idempotency records expire after 24 h. |
| `orders` | `/userId` | `order` | Order history is always one shopper, newest first. |
| `migrations` | `/id` | migration records | One record per data migration. |

Indexing: everything is indexed except `/description/?` (large, never filtered); composite indexes back the two multi-field sorts (`nameLower asc, id asc` and `createdUtc desc, id desc`).

**Hot partitions and limits.** `inventory` is a single logical partition by design. That caps it at 20 GB and at one physical partition's throughput (about 10,000 RU/s). For a merchandise store with thousands of products and modest order rates this is far away, and it buys atomic multi-product checkout without a saga. The exit (ADR-004) is a partition per franchise. Carts and orders spread across shoppers, so they have no hot key.

**Cross-partition queries and RU cost.** Point reads (product page, cart, order) cost about 1 RU. Stock for a page of products is one `ReadMany`. Catalogue search without a franchise filter fans out across franchise partitions; with a filter it is one partition. The catalogue is small, the query is served by the composite index, and pages are capped at 50, so the fan-out stays cheap. The total count is computed only for the first page.

**Paging.** Seek (keyset) pagination: each page is `SELECT TOP pageSize+1 ... WHERE (sortKey, id) > (last row) ORDER BY sortKey, id`. The cost of a page does not grow with depth, the page size is enforced by the query itself, and the token is just the last row's sort key, signed with HMAC-SHA256, bound to the filters it was issued for, and valid for 60 minutes (with key rotation support). This replaced Cosmos DB's own continuation tokens after testing showed the emulator ignores `MaxItemCount` on `ORDER BY` queries, and because native ORDER BY tokens are opaque engine state.

**Consistency.** Session consistency (the account default) with one `CosmosClient` per process. Correctness never depends on read freshness: every write that matters is conditional (ETag `If-Match`, patch filter predicates, create-if-absent), so a stale read can only cause a refused write, never a wrong one.

**Consequences.** Atomic checkout and cheap reads; the single inventory partition is the scaling ceiling and is called out as the first thing to change for multi-tenancy.

## ADR-003: Concurrency and idempotency

**Context.** Shoppers double-click, networks drop responses, two shoppers race for the last unit, and admins edit the same product at the same time.

**Decision.**

- **Stock never goes below zero.** Checkout is one transactional batch in the `inventory` partition: create the marker `deduct:{orderId}`, create a `purchase:{user}:{product}` record for each limited edition, then for each line a patch `available -= qty` with the filter `available >= qty`, plus an audit row per line (reason `order:{orderId}`). If any operation fails, none is applied. Lines are patched in a fixed order. The failing lines are identified by re-reading stock, returned as `failedLines` in a `409 out-of-stock`, and the cart is kept.
- **A retried checkout returns the same order.** The client creates the order id (a GUID) and keeps it, even across a reload, until it gets a final answer. The marker id is derived from it, so a repeat fails on operation 0 and is recognised as a replay; the order document uses the same id with create-if-absent. Answers: `201` the first time, `200` with the same order after.
- **A checkout interrupted between the stock deduction and the order record is finished.** The marker carries the full order snapshot. A background sweeper (every 30 s, for markers older than 120 s) creates the missing order. Stock is never deducted without an order eventually existing.
- **Cart changes apply once.** Every change carries a client-generated `Idempotency-Key`. The API stores `req:{key}` with a fingerprint of the request and the response, in the **same batch** as the cart change, which is conditional on the cart's ETag. A repeat returns the stored response; the same key with a different request is `409 idempotency-key-reused`; a missing key is `400`.
- **Price and stock edits use optimistic concurrency.** Admin `GET` returns separate ETags for price and stock; `PUT` requires `If-Match` (`428` without it, `412` when stale, with the current ETag in the response). The change and its audit row (who, when, old, new, reason) commit in one batch.
- **Price changes after adding to the cart are never applied silently.** A cart line keeps the price the shopper accepted. When the current price differs, the cart is flagged (`requiresAcceptance`), the shopper accepts the new total explicitly (refused if it moved again), and checkout requires the accepted total to equal the current total (`409 price-changed` with `currentTotalPaise` otherwise).
- **Limited editions.** Signed photographs have `maxPerCustomer = 1`, and the purchase record in the checkout batch makes "one per customer" hold across orders, not just within one cart.

**Alternatives considered.** Reserving stock when items are added to the cart (fair for limited editions, but carts are abandoned and reservations need expiry and cleanup; rejected for now, see NOTES). A distributed lock (unnecessary given the atomic batch). Optimistic retry loops on a stock document read-modify-write (more round trips and contention than a conditional patch).

**Consequences.** The walkthrough's race ("only one order gets the photo") holds under any interleaving and is tested with concurrent requests. The price of atomic multi-product checkout is the single inventory partition (ADR-002).

## ADR-004: Evolution to a store per franchise (multi-tenancy) — design only

**Context.** Each franchise will manage its own catalogue, stock and sales reports, with data isolated from other franchises.

**Decision (proposed).** Make the franchise the tenant everywhere and enforce it in three places: data, API and authorisation.

- **Data model and partition keys.** `tenantId` becomes the franchise code on every document (today it is the constant `default`).
  - `products` already partitions by `/franchiseId`, so it is already tenant-shaped.
  - `inventory` keeps `/tenantId`, which now gives each franchise its own logical partition. This also removes today's single-partition ceiling. Checkout batches then run per franchise, so a cart spanning franchises becomes one order per franchise, grouped by the `checkoutId` field that is already stored.
  - `orders` moves to a hierarchical partition key `[/tenantId, /userId]`. That keeps "my orders" a single-partition read and lets a franchise's sales reports query its own prefix without a fan-out. Reports would be built from the change feed into per-tenant aggregates, reintroducing a lease container deliberately.
- **API.** Tenant-scoped admin routes in a new version, e.g. `/api/v2/stores/{storeId}/products/...` and `/api/v2/stores/{storeId}/reports/sales`. Shopper browsing stays cross-store. v1 keeps working during the transition.
- **Authorisation.** Entra ID app roles per store (`StoreAdmin`) in a `stores` claim. A resource-based authorisation handler checks the route's `storeId` against the claim. Below the API, every store operation takes a `TenantId` value object and always queries with it as the partition key, so a missing tenant check cannot read another franchise's data.
- **Migrating existing data.**
  - Partition keys cannot change in place. New containers (orders with the hierarchical key) come from Bicep. A DbMaintenance migration copies the data idempotently. The API then reads both, switches over and retires the old container.
  - Inventory documents move from `tenantId: default` to their franchise. Changing a partition key value is a delete plus create, done per document by an idempotent migration.
  - Every step is recorded in the `migrations` container.

**What I would do first.**

1. Introduce `TenantId` in the Application ports, defaulting to `default`, so the code is tenant-aware before any data moves.
2. Add the `stores` claim and the authorisation handler.
3. Migrate inventory to per-franchise partitions and split checkout per franchise.
4. Then move orders and add the v2 admin routes.

This order removes the scaling ceiling early and keeps every step reversible.

## API design

- **Versioning.** All routes are under `/api/v1`. Within v1, changes are additive only: no field is removed, renamed or changes meaning. A breaking change would be a new `/api/v2` route served side by side while clients move.
- **The backwards-compatible change made.** Order responses gained an optional `itemCount` field. The API now returns an explicit `OrderResponse` contract instead of the domain type, so domain changes cannot leak into the contract. Existing clients are protected in three ways:
  - Clients ignore unknown fields (the TypeScript model marks `itemCount` optional, and the UI falls back to summing the lines).
  - Every original field is still present and unchanged.
  - An integration test (`Orders_keep_every_v1_field_and_add_item_count_backwards_compatibly`) fails if any v1 field disappears.
- **Errors.** RFC 7807 problem details with a stable `code` and a `correlationId`:
  - `400` validation, with per-field `errors`
  - `401` and `403`
  - `404`
  - `409` out-of-stock, price-changed, idempotency-key-reused and product-exists
  - `412` stale edit
  - `428` missing `If-Match`
  - `429` with `Retry-After`
  - `503` with `Retry-After` when the database is unavailable
- **Pagination.** Capped page size (1 to 50, default 20), stable order, signed seek tokens (ADR-002).
- **Idempotency.** `Idempotency-Key` on cart changes; client-chosen ids for orders (`PUT /orders/{id}`) and products (`POST` with a slug id).
- **Rate limits.** Search 120 per minute and checkout 10 per minute, per signed-in user (per IP when anonymous), with `429` and `Retry-After`. Limits are read per request from options, so per-environment settings apply.

## Reliability

- **Timeouts:** Cosmos DB requests time out after 10 s (`CosmosDb:RequestTimeoutSeconds`).
- **Retries with back-off:** the SDK retries throttled (429) requests up to 5 times, within 15 s, using the back-off Cosmos DB asks for. Writes are idempotent, so the UI can offer "Try again" safely, and it does for retryable failures (network, 429, 5xx).
- **Graceful degradation:** throttling or timeouts become `503` with `Retry-After` and the problem code `dependency-unavailable`. Every page shows a loading state and an error state with a retry. Browsing does not need sign-in.
- **Health checks:** `GET /api/health` (liveness) and `GET /api/health/db` (Cosmos DB reachable, `503` otherwise).
- **Recovery:** the checkout sweeper (ADR-003). Data migrations are claimed through ETags, recorded, and safe to re-run.

## Security

- **Authentication:**
  - The API validates JWT bearer tokens (issuer, audience, signature, lifetime).
  - Locally, `POST /api/dev/token` issues tokens. It exists only in the Development environment and returns 404 elsewhere.
  - In real environments an identity provider such as Entra ID issues the tokens.
- **Authorisation:** shopper endpoints use the token's `sub` as the user id, so a shopper can only reach their own cart and orders. Admin endpoints require the `admin` role.
- **Secrets:**
  - Signing keys come from configuration. Development-only placeholders live in `appsettings.Development.json`; real values come from environment variables or Key Vault.
  - Keys under 32 characters stop the app at startup.
  - In Azure, Cosmos DB is reached with Entra ID: the Bicep template disables account keys and grants the Data Contributor role, which cannot change containers.
- **Input validation:**
  - Data annotations plus service-level validation that reports every invalid field at once.
  - Ids are checked against Cosmos DB's id rules (no `/ \ ? #`).
  - All queries are parameterised.
  - Continuation tokens are signed, so they cannot be forged or replayed with other filters.
- **Logs:** request bodies, tokens and keys are never logged. Logs carry the correlation id and opaque user ids only.

## Observability

- **Structured logs:** JSON lines outside Development, with scopes, so every entry carries its `CorrelationId`.
- **Correlation ids:** `X-Correlation-Id` is accepted or generated, echoed in the response, and included in every problem details body, so a user-reported error can be found in the logs.
- **Metrics** (`System.Diagnostics.Metrics`, meter `IplFranchise`):
  - checkout outcomes (confirmed, replayed, price-changed, out-of-stock, limit-reached)
  - idempotent replays
  - orders completed by the sweeper
  - RU charge per Cosmos DB request
- **Tracing:** the correlation id is tagged on the current `Activity`. Exporting traces and metrics with OpenTelemetry is the next step (see NOTES).

## Front end

- **Typed API:** models in `core/models.ts`, and one API service layer (`ShopApi`, implemented by `HttpShopApi`), so components never build URLs.
- **State:** signal-based state services for the cart and checkout.
- **Idempotency in the client:**
  - One idempotency key per user intent, reused on retry and replaced only after a final answer.
  - The order id is kept in `sessionStorage` until the server confirms or rejects the order.
  - Buttons are disabled while a request is in flight, so a double click cannot send twice.
- **States shown:** loading, error with retry, validation, out of stock, conflict and price-changed, each in an announced region (`role="alert"` or `role="status"`).
- **Formatting:** rupees through one formatter; times shown in IST by default, with an option to use the device's zone.
- **Accessibility:** labelled form controls, keyboard operation, a skip link, focus moved to the page after navigation, and a live region for announcements.

## Testing

| Suite | What it covers | Runs against |
| --- | --- | --- |
| `UnitTests` | Domain rules at their boundaries (stock of exactly 1, quantities 1, 10 and 11), cart idempotency, checkout decisions, recovery, validation, paging tokens | in-memory fake |
| `DbMaintenance.Tests` | Schema contract, drift detection, migration claim policy, second run changes nothing, migrations run twice give the same documents, transactional batch rollback | unit tests and the emulator |
| `IntegrationTests` | The real API in memory against the emulator, with data from the real migrations: the Example Walkthrough step by step, concurrent checkouts for the last unit, a burst of identical checkouts, 412 on stale edits, idempotent cart changes, a partial failure leaving consistent state, paging and ordering over long product and order lists, problem details, the v1 contract | the emulator, nothing mocked |
| Frontend (Vitest) | Components (rejected actions, double submit, retry, loading, error, conflict and stale states, accessibility checks), the HTTP client contract (URLs, bodies, headers, problem mapping), time-zone conversion | stubbed `ShopApi` and `HttpTestingController` |

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs all of these, with the emulator as a service container. There, the emulator tests fail instead of skipping when it is unreachable.
