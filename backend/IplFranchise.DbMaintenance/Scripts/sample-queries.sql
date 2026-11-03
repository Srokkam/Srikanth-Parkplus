-- Cosmos DB for NoSQL queries against the ipl-store database.
-- Run them in Data Explorer (emulator: http://localhost:1234, Azure: portal > Data Explorer), one at a time,
-- in the container named above each query. Sample data comes from the "seed" command.
--
-- Rules every query follows:
--   * Give the partition key (franchiseId, tenantId, userId) so the query stays in one partition.
--   * Filter on docKind: products, inventory and carts each hold more than one kind of document.
--   * In code, pass values as parameters (@franchiseId), never by concatenating strings, and page with
--     continuation tokens, never OFFSET.

-- products: one franchise's catalogue, A to Z (served by the nameLower ASC, id ASC composite index)
SELECT c.id, c.name, c.pricePaise, c.limitedEdition, c.maxPerCustomer
FROM c
WHERE c.franchiseId = 'csk' AND c.docKind = 'product'
ORDER BY c.nameLower ASC, c.id ASC

-- products: search by name within a franchise
SELECT c.id, c.name FROM c
WHERE c.franchiseId = 'kkr' AND c.docKind = 'product' AND STARTSWITH(c.nameLower, 'kkr s') = true

-- products: price history of one product (audit rows live next to the product)
SELECT c.oldValue, c.newValue, c.reason, c.actor, c.tsUtc FROM c
WHERE c.franchiseId = 'csk' AND c.docKind = 'audit' AND c.productId = 'csk-cap'
ORDER BY c.tsUtc DESC

-- inventory: stock levels (one logical partition: tenantId 'default')
SELECT c.productId, c.available FROM c
WHERE c.tenantId = 'default' AND c.docKind = 'stock'

-- inventory: checkout markers the sweeper still has to finalise
SELECT c.id, c.orderId, c.userId, c.createdUtc FROM c
WHERE c.tenantId = 'default' AND c.docKind = 'deduct' AND c.orderCreated = false

-- inventory: limited-edition purchases by one user
SELECT c.productId, c.orderId FROM c
WHERE c.tenantId = 'default' AND c.docKind = 'purchase' AND c.userId = 'asha'

-- carts: one user's cart
SELECT * FROM c WHERE c.userId = 'asha' AND c.docKind = 'cart'

-- orders: one user's history, newest first (served by the createdUtc DESC, id DESC composite index)
SELECT c.id, c.status, c.createdUtc, c.totalPaise FROM c
WHERE c.userId = 'asha' AND c.docKind = 'order'
ORDER BY c.createdUtc DESC, c.id DESC

-- migrations: what has run, and anything failed or stuck
SELECT c.id, c.status, c.startedUtc, c.completedUtc, c.error FROM c ORDER BY c.id
