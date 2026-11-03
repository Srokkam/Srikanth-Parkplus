# IPL Franchise

Angular frontend + .NET Web API backend backed by Azure Cosmos DB (NoSQL API): the IPL merchandise store.

| Document | What it covers |
| --- | --- |
| [DESIGN.md](DESIGN.md) | Four ADRs (architecture, data model and partition keys, concurrency and idempotency, multi-tenancy evolution), plus API, reliability, security, observability and testing |
| [NOTES.md](NOTES.md) | Assumptions, extra scenarios, known limitations, next steps |
| [AI-USAGE.md](AI-USAGE.md) | AI tools used and where they helped |

```
.
├── docker-compose.yml                       The whole system with one command
├── .github/workflows/ci.yml                 Build + all tests (emulator as a service container) + Docker images
├── frontend/                                Angular 21 app (standalone components, signals, SCSS, routing)
│   ├── Dockerfile, nginx.conf               Production build served by nginx, /api proxied to the API
└── backend/
    ├── Dockerfile                           API and DbMaintenance images (--target api | dbmaintenance)
    ├── IplFranchise.sln
    ├── global.json                          Opts dotnet test into Microsoft.Testing.Platform
    ├── nuget.config                         Restores from nuget.org only
    ├── docker-compose.yml                   Just the Cosmos DB emulator, for running the API from source
    ├── IplFranchise.Domain/                 Business rules: catalogue, cart, pricing, checkout, limits
    ├── IplFranchise.Application/            Use cases (catalogue, cart, checkout, orders, admin) and their ports
    ├── IplFranchise.Infrastructure/         Cosmos DB stores behind those ports
    ├── IplFranchise.Api/                    .NET 10 Web API: controllers, auth, problem details, checkout sweeper
    ├── IplFranchise.DbMaintenance/          Owns everything about the Cosmos DB schema and data migrations
    │   ├── Schema/schema.json               THE declaration: database, containers, keys, TTL, indexing
    │   ├── Schema/                          verify-schema, local provisioning, spec <-> SDK mapping
    │   ├── Migrations/                      0001-baseline, 0002-backfill-limited-edition, 0003-initial-catalogue
    │   ├── Core/                            Runner, claim policy, DocumentOperations, client factory
    │   ├── Infrastructure/main.bicep        Account, database and containers for real environments
    │   ├── Scripts/sample-queries.sql       Data Explorer queries that follow the query rules
    │   └── Program.cs                       CLI
    ├── IplFranchise.DbMaintenance.Tests/    Unit tests + emulator tests (the verification gates)
    ├── IplFranchise.UnitTests/              Domain and service tests (no database)
    └── IplFranchise.IntegrationTests/       The real API against the emulator, data from the real migrations (incl. the walkthrough)
```

## Prerequisites

- .NET SDK 10
- Node.js 20.19+ / 22.12+ / 24+ and npm
- Docker (for the Cosmos DB emulator) or an Azure Cosmos DB account

## Run it

**Everything with one command** (needs only Docker), from the repository root:

```bash
docker compose up --build
```

| | |
| --- | --- |
| Store | http://localhost:8080 |
| API (Swagger) | http://localhost:5220/swagger |
| Cosmos DB Data Explorer | http://localhost:1234 |

`db-setup` waits for the emulator, creates the database and containers, and runs the data migrations, then the API and the store start. `docker compose down` removes everything. Ports 8080, 5220, 8081 and 1234 must be free; stop any other emulator first.

**From source** (for development), commands from `backend/` unless noted:

```bash
docker compose up -d                                                   # 1. emulator: http://localhost:8081, Data Explorer http://localhost:1234
dotnet run --project IplFranchise.DbMaintenance -- setup-local         # 2. database, containers and data migrations
dotnet run --project IplFranchise.Api --launch-profile http            # 3. API: http://localhost:5220/swagger
cd ../frontend && npm install && npm start                             # 4. UI: http://localhost:4200
```

**Data.** Migration `0003-initial-catalogue` adds the walkthrough's catalogue with its opening stock: MI Jersey ₹1,999.00 (50), CSK Cap ₹499.00 (100), RCB Flag ₹299.00 (200), KKR Signed Photo ₹4,999.00 (2, limited edition, one per customer). It is create-if-absent, so it never overwrites changes. Admins add more products at `/admin/products/new` and change price or stock (with the audit trail) from the link on each product page.

**Try the walkthrough in the UI.**

1. Choose 2 per page.
2. Filter by type Cap and franchise CSK.
3. Add 2 MI Jerseys, 3 CSK Caps and a KKR Signed Photo (double-clicking is safe).
4. Place the order.
5. Raise the CSK Cap to ₹549 on its admin page.
6. Open Orders: the order keeps ₹499.00 per cap, and times are shown in IST.

The emulator is plain HTTP, so `https://` in the browser gives `ERR_SSL_PROTOCOL_ERROR`. The default connection string in both projects' `appsettings.json` is the emulator's well-known public key.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/v1/products?q=&franchise=&type=&pageSize=&continuationToken=` | Search the catalogue; filters combine, pages are ordered by name (the home page) |
| `GET /api/v1/products/{franchiseId}/{productId}` | One product with its live stock status |
| `GET /api/v1/franchises` | The franchise codes used by products in the catalogue (empty until products exist) |
| `GET /api/v1/cart`, `POST /api/v1/cart/items`, `PUT`/`DELETE /api/v1/cart/items/{productId}`, `POST /api/v1/cart/accept-changes` | The shopper's cart; every change needs an `Idempotency-Key` |
| `PUT /api/v1/orders/{orderId}` | Checkout with a client-chosen order id: `201` first time, `200` with the same order on a retry |
| `GET /api/v1/orders?from=&to=&pageSize=&continuationToken=`, `GET /api/v1/orders/{orderId}` | Order history, newest first, filtered by UTC date range |
| `POST /api/v1/admin/products` | Admin: create a product with its opening stock. The caller picks the id (e.g. `csk-cap`), so a repeat answers `409 product-exists` |
| `GET /api/v1/admin/products/{f}/{p}`, `PUT .../price`, `PUT .../stock`, `GET .../audit?field=price` | Admin: edits need `If-Match` (`428` without, `412` when stale); every change is audited |
| `GET /api/health`, `GET /api/health/db` | API up; Cosmos DB reachable (`503` otherwise) |

Examples for every call are in [IplFranchise.Api.http](backend/IplFranchise.Api/IplFranchise.Api.http) and Swagger.

**Development sign-in.** There is no identity provider yet. In Development the API issues tokens at `POST /api/dev/token`, and the UI signs in as `dev-customer` for the cart and orders and as `dev-admin` for admin calls.

**Paging** is keyset ("seek") based, so the page size is enforced by the query itself and holds on the emulator too (which ignores the page size of Cosmos DB's own ORDER BY paging). See ADR-002 in [DESIGN.md](DESIGN.md).

## Database: `ipl-store`

Declared once in [`Schema/schema.json`](backend/IplFranchise.DbMaintenance/Schema/schema.json). The maintenance tool verifies against it and `main.bicep` deploys from it, so there is no second copy to drift.

| Container | Partition key | Documents (`docKind`) | Default TTL | Custom indexing |
| --- | --- | --- | --- | --- |
| `inventory` | `/tenantId` (always `default`) | `stock`, `audit`, `deduct`, `purchase` | off | default |
| `products` | `/franchiseId` | `product`, `audit` (price) | off | exclude `/description/?`; composite `nameLower` asc + `id` asc |
| `carts` | `/userId` | `cart`, `idempotency` | 30 days | default |
| `orders` | `/userId` | `order` | off | composite `createdUtc` desc + `id` desc |
| `migrations` | `/id` | migration records | off | default |

**`leases` is deliberately not created.** Checkout uses a sweeper, not the change feed, so nothing would write to it. `verify-schema` reports it as drift if it appears. To bring it back for a change-feed read model, add it to `schema.json`.

Conventions: money is integer paise (`long`), `currency` is `INR`, instants are UTC ISO 8601 ending in `Z`, property names are camelCase, and every query filters on `docKind`.

## DB maintenance

Two jobs, kept apart:

| Job | What | Done by |
| --- | --- | --- |
| Schema | Database, containers, partition keys, indexing, TTL, throughput | `Infrastructure/main.bicep` in real environments (pipeline, with approval); `provision` on the local emulator only |
| Data | Document contents: backfill, rename, recompute | Migrations, run by `migrate` in every environment |

```bash
dotnet run --project IplFranchise.DbMaintenance -- <command>
```

| Command | Does | Where |
| --- | --- | --- |
| `provision` | Creates the database and any **missing** containers, then verifies. Never modifies an existing container. | Emulator only |
| `verify-schema` | Compares every container with `schema.json` (partition key, TTL, excluded paths, composite indexes, unexpected containers). | Anywhere; run after every deployment |
| `migrate` | `verify-schema`, then pending migrations in id order. Refuses to run on drift. | Anywhere |
| `setup-local` | `provision` + `migrate` (including the opening catalogue). A second run creates and changes nothing. | Emulator only |
| `status` | Each migration's record: pending, running, completed or failed, with timestamps and error. | Anywhere |

Exit codes: `0` success, `1` error, `2` schema drift, `3` another runner is applying a migration.

### How a migration runs

Each migration has a record in the `migrations` container (`id`, `status`, `description`, `startedUtc`, `completedUtc`, `error`):

1. **Completed:** skip it.
2. **No record:** create one as `running`. A conflict means another runner won, so stop.
3. **Running for less than 15 minutes:** stop. **Failed, or running for longer:** take it over with a replace conditional on the record's `_etag`, so two takers can't both win.
4. Run `ApplyAsync`. It must be safe to run again from the start.
5. Mark the record `completed`, again conditional on the ETag.
6. **On an exception:** record `failed` with the message and exit non-zero. The next run retries.

### Adding a migration

Add a class under `Migrations/` with the next id. It's discovered automatically.

```csharp
// Migrations/0003_BackfillProductSlug.cs
using IplFranchise.DbMaintenance.Core;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Migrations;

public sealed class BackfillProductSlug : Migration
{
    public override string Id => "0003-backfill-product-slug";
    public override string Description => "Set slug = id on products that lack it";

    public override Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken) =>
        db.TransformItemsAsync(
            "products",
            new QueryDefinition("SELECT * FROM c WHERE c.docKind = 'product' AND NOT IS_DEFINED(c.slug)"),
            product => { product["slug"] = product["id"]!.GetValue<string>(); return true; },
            cancellationToken);
}
```

Rules:

- Make it idempotent by construction.
- Never edit a migration that has run anywhere.
- Filter on `docKind`.
- Use expand, migrate, contract: ship readers that tolerate a missing field before backfilling it.

`DocumentOperations` only changes documents, never containers. Its helpers:

| Helper | Use |
| --- | --- |
| `SetIfMissingAsync` | Backfill a field: patch with a `NOT IS_DEFINED` filter |
| `RenamePropertyAsync`, `RemovePropertyAsync` | Patch `move` / `remove`, only where still needed |
| `PatchItemIfAsync` | One document, conditional filter |
| `TransformItemsAsync` | Anything else, as a replace conditional on the ETag; refuses to change a partition key value |
| `CreateIfAbsentAsync` | Insert with a fixed id; a 409 means it already exists |

Bulk helpers page with continuation tokens (`PageSize`, default 100) and cap concurrent writes (`MaxConcurrency`, default 8). The SDK retries 429s.

## Tests

```bash
dotnet test --solution IplFranchise.sln     # from backend/: all backend tests
cd ../frontend && npm test                  # Angular tests (Vitest)
```

Tests that need Cosmos DB skip when no emulator is reachable (set `REQUIRE_COSMOS_EMULATOR=true`, as CI does, to make that a failure). They read `CosmosDb__ConnectionString` (default `localhost:8081`), and every test class uses its own throwaway database, set up by the real provisioning and data migrations. Nothing in the integration tests is mocked.

- **Integration tests:** the real API in memory against the emulator.
  - `WalkthroughTests` replays the assessment's Example Walkthrough step by step, including the concurrent checkout for the last photo.
  - `ShopFlowTests` covers races for the last unit, bursts of identical checkouts, idempotency, `412` and `428`, price acceptance, sweeper recovery and quantity and stock boundaries.
  - `CatalogueTests` covers paging, search, filters and token tampering.
  - `LargeListPagingTests` pages 49 products and 14 orders, with date ranges.
  - `CatalogueAdminTests` covers product creation.
- **Frontend tests:**
  - components against stubbed API answers: rejected actions, double submit, retry, loading, error and conflict states, accessibility
  - the HTTP client contract with the backend: URLs, bodies, headers, problem details
  - time-zone conversion

DbMaintenance tests:

- **Unit tests (no database):**
  - `schema.json` against the container contract
  - schema comparison, and that `provision` creates exactly what `verify-schema` expects
  - claim decision table, migration ids
  - the local-only guard
- **Emulator tests:** these skip when no emulator is reachable. They point at `CosmosDb__ConnectionString`, default `localhost:8081`, and each test uses its own throwaway database.
  - a second setup run changes nothing
  - each migration run twice gives the same documents
  - the backfill touches only `product` documents
  - drift is reported, and `provision` never repairs it by modifying containers
  - the runner's busy, takeover and failure-retry behaviour
  - **transactional batch rollback:** a checkout batch whose conditional stock patch fails leaves no marker, no audit row and unchanged stock, and a repeated order id conflicts

The emulator ignores request units and may not enforce custom indexes. Composite-index behaviour and RU cost still need checking against a real sandbox account.

## Real environments (Azure)

```bash
az deployment group create -g <rg> -f IplFranchise.DbMaintenance/Infrastructure/main.bicep \
  -p accountName=<name> dataContributorPrincipalIds='["<api-identity-object-id>","<pipeline-identity-object-id>"]'

export CosmosDb__ConnectionString="AccountEndpoint=https://<name>.documents.azure.com:443/;"   # no key: Entra ID
dotnet run --project IplFranchise.DbMaintenance -- migrate
```

The template sets:

- **Consistency:** Session.
- **Throughput:** serverless by default; set `serverless=false` for autoscale.
- **Backup:** `backupPolicyType` (Continuous can't be turned off later).
- **Keys:** account keys are disabled, and the listed identities get the Data Contributor role, which can change documents but not containers.
- **Lock:** a `CanNotDelete` lock on the account.

A connection string without `AccountKey` signs in with `DefaultAzureCredential`, in both the API and the tool.

## Design questions (from the container reference)

1. **Who deploys containers, and who runs data migrations?** Containers come from `main.bicep` through the pipeline with approval. Data migrations run with `migrate` as a pipeline step under an identity with the Data Contributor role, which can't create containers.
2. **What if a migration stops halfway through 100,000 documents?** Its record says `failed`, or stays `running` until it goes stale after 15 minutes. The next run takes it over and starts again. Set-if-missing filters mean only the remaining documents are touched.
3. **How do we prove a second run changes nothing?** `Second_setup_run_creates_and_changes_nothing` and `Each_migration_run_twice_gives_the_same_documents` compare full document snapshots.
4. **Which migrations touch more than one partition?** `0002` patches products across franchises. Each patch is a single-partition point write, so a failure leaves some products done and some not. The rerun finishes the rest; nothing assumes cross-partition atomicity.
5. **What if a partition key is wrong in six months?** Partition keys can't change in place. Add a new container to `schema.json` and Bicep, copy with a migration (`TransformItemsAsync` / `CreateIfAbsentAsync`), read from both during a switch-over, then remove the old container from `schema.json` and Bicep.
