# Notes: assumptions, extra scenarios, known limitations, next steps

## Assumptions

- **Money.** Every amount is an integer number of paise, so ₹ amounts have exactly two decimals by construction and there are no floating-point rounding errors. Prices include tax, and the only currency is INR.
- **Shipping.** It is modelled (`shippingPaise` on the cart and the order, shown as "Free") but always ₹0 in this version. That keeps the walkthrough's totals exact; a rule such as "free over ₹999" would go in `CartPricing`.
- **Opening catalogue.** The four walkthrough products are added by data migration `0003-initial-catalogue`, with their opening stock and an audit row for it. The migration is create-if-absent, so it never overwrites a price or stock level an admin has changed.
  - The walkthrough doesn't price the RCB Flag, so it is ₹299.00.
  - The KKR Signed Photo is a limited edition, one per customer.
- **Product ids** are slugs starting with the franchise code (e.g. `csk-cap`). The stock record shares the id, so the prefix keeps ids unique across franchises. Ids are never reused.
- **Default order.** Products are listed by name A to Z, then by id, which is stable. The walkthrough's first page lists "MI Jersey and CSK Cap"; ours lists CSK Cap and KKR Signed Photo. The behaviour it describes holds: two of four products, the total, and a next page.
- **The walkthrough's photo stock.** It starts the KKR Signed Photo at 2 but ends at 0 after one order, while Ravi holds "the last" one. The walkthrough test models one earlier sale (another fan buys one), so the last unit is genuinely contested. Either shopper can win the race; the test checks the walkthrough's numbers when Asha wins and the mirror image when Ravi wins.
- **Identity.**
  - A user is the token's `sub`, and each user has one cart.
  - Locally a development-only endpoint issues tokens. The UI signs in as `dev-customer` for shopping and `dev-admin` for admin pages.
  - Integration tests use named shoppers (Asha, Ravi, Kiran).
- **Stock in the cart is advisory.** Adding to the cart checks current stock but reserves nothing. Checkout is the single atomic check.
- **Stock changes after adding** are flagged when they make a line unfulfillable (only N left, or out of stock), and the shopper must reduce or remove the line before checking out. **Price changes** are always flagged and must be accepted explicitly.
- **Limited editions.** At most `maxPerCustomer` units per order, and one purchase per customer across all orders, enforced in the checkout batch.
- **Times.** Stored in UTC and shown in IST by default; a shopper can switch to their device's zone. Order history date filters are whole calendar days in the chosen zone. "To" is inclusive in the UI and sent to the API as the start of the next day (exclusive).
- **Franchises** are the codes products use (`GET /api/v1/franchises` reads them from the catalogue). Franchise names are not stored, so the UI shows codes (CSK, MI, ...).

## Extra scenarios and capabilities (beyond the minimum)

| Added | Why |
| --- | --- |
| Interrupted-checkout recovery: a marker holding the order snapshot, completed by a background sweeper | A crash between "stock deducted" and "order saved" would otherwise take a customer's stock without an order. |
| One limited edition per customer across orders (purchase record in the checkout batch) | `maxPerCustomer` alone only limits a single cart. |
| Reusing an idempotency key with a different request returns `409 idempotency-key-reused` | Silently replaying the old response for a different request would hide a client bug. |
| Accepting new prices is guarded (refused if the total moved again since the shopper looked) | Prevents accepting a total the shopper never saw. |
| Signed, filter-bound, expiring continuation tokens | Tampering with a token or reusing it with other filters returns `400` instead of leaking or mixing pages. |
| Seek (keyset) pagination | Bounded cost per page, and the page size cap holds on the emulator too (it ignores `MaxItemCount` on `ORDER BY`). |
| Admin: create a product with its opening stock; a price and stock page with conflict handling and the audit history | Makes "an admin raises the price" and optimistic concurrency usable end to end in the UI. |
| `428` when `If-Match` is missing (in addition to `412` when stale) | An edit without a version must not be applied blindly. |
| Rate limits per user (per IP when anonymous), read per request from configuration | Protects checkout and search; environment-specific limits apply without a restart. |
| Correlation id in every problem details body | A user can quote it, and support can find the request in the logs. |
| Schema as data, `verify-schema` drift detection, migrations claimed by ETag, Bicep with keys disabled and a delete lock | Containers are never created by the running API, and a typo or a changed index is caught before data migrations run. |
| Order history page: date range and time-zone choice; "Per page" choice on the product list | Requirement for orders; the page size choice demonstrates paging with four products. |
| One-command Docker Compose and a CI pipeline running the emulator tests | A reviewer can run everything, and the emulator tests cannot silently skip in CI. |

## Known limitations

- **Emulator fidelity.** The local emulator ignores request units and may not enforce custom indexes. Composite-index behaviour, RU cost and throttling need checking against a real (sandbox) account.
- **Single inventory partition.** It is capped at 20 GB and one physical partition's throughput (ADR-002). This is fine for this store and is the first thing ADR-004 changes.
- **No identity provider.** Locally, the development token endpoint stands in for one, and the UI uses fixed development identities. A two-shopper race is demonstrated by the integration tests rather than in the UI.
- **No stock reservation.** A shopper can learn at checkout that the last unit was sold. This is deliberate (see ADR-003), and the UI explains it and keeps the cart.
- **Search.** `CONTAINS` on `nameLower`; an unfiltered search fans out across franchise partitions. Fine for this catalogue; a large one would use a search index.
- **Order history has no total count.** Deliberately, as the extra query would cost RUs on every first page.
- **Admin audit history** uses Cosmos DB's own continuation tokens; on the emulator it arrives in one page.
- **The sweeper runs in every API instance.** That is safe because recovery is idempotent, but redundant at scale; a single worker or leader election would be better.
- **Continuation tokens expire after 60 minutes**, so a very long browsing session restarts paging from the first page.
- **Front-end test runner.** The tests use Vitest, Angular 21's default, which has a Jest-compatible API, rather than Jest or Jasmine.
- **No browser end-to-end suite in the repository.** The walkthrough is automated at the API level (`WalkthroughTests`) and was also run manually through the UI in a browser.

## What I would do next

1. Export traces and metrics with OpenTelemetry, and alert on checkout outcomes (out-of-stock and price-changed rates), 429s and RU per request.
2. Add a real identity provider (Entra ID) with per-store admin roles: the first step of the multi-tenancy plan (ADR-004).
3. Add a Playwright suite for the walkthrough in CI.
4. Load-test checkout contention on a real account (k6), then tune autoscale throughput.
5. Consider short-lived stock reservations for limited editions.
6. Add shipping rules, promotions and franchise display names.
