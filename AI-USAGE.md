# AI usage

## Tools

- **Claude Code** (Anthropic), used in VS Code throughout the project.

## Where it helped

- **Scaffolding:** the Angular workspace, the .NET solution and projects, `nuget.config`, the Docker Compose emulator setup.
- **Database tooling:** the `IplFranchise.DbMaintenance` project from a container reference document. That covers the schema declaration, `verify-schema`, local provisioning, the migration runner (claims, stale takeover, failure records), the data migrations including the opening catalogue, and the Bicep template.
- **Backend:** API features and fixes, including:
  - product creation with opening stock
  - franchises derived from the catalogue
  - the `OrderResponse` contract with the backwards-compatible `itemCount` field
  - Cosmos DB timeouts and retries
  - rate limits read per request
  - structured JSON logging
- **Frontend:** wiring the UI to the real API, the admin pages (create product; price and stock with conflict handling and audit history), order history date filters and IST display, and the page-size choice.
- **Tests:**
  - unit and emulator tests for the migration tooling
  - integration tests that load data through the real migrations, including the Example Walkthrough step by step and large-list paging
  - front-end component and API-client tests
- **Debugging:** found by running against the emulator:
  - request records whose validation attributes made sign-in, add-to-cart and admin edits return 500
  - router inputs filling the search box with "undefined"
  - the emulator ignoring page size on `ORDER BY` queries, which led to seek pagination
  - rate limits not picking up configuration overrides
- **Delivery and documentation:** Dockerfiles, the one-command compose file, the CI workflow, and first drafts of README, DESIGN and NOTES.

## How the output was checked

- Every change was built and run. All automated tests run against the Cosmos DB emulator with no mocks in the integration tests.
- The walkthrough was also run through the real UI in a browser against the composed system.


