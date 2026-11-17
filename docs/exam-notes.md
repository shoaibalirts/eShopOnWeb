# Exam notes

Questions and ideal answers from building my project, grouped by the six exam topics.
Each answer uses my own code as the example.

## 1. Microservices vs monolith

### Inventory and the monolith share one SQL Server container. Is that still Database per Service? (17/11)

Yes. The rule is about **ownership and access**, not about hardware: only Inventory may read or write `InventoryDb`, and the monolith gets stock data only through Inventory's API or messages. A *separate database* on a shared instance (`Initial Catalog=InventoryDb` vs `Microsoft.eShopOnWeb.CatalogDb`) keeps the schemas apart, so each service can change its own tables without breaking the other.

The trade-off: a shared instance is a shared **failure point and resource pool**. If the SQL container dies, both go down, and one heavy query can slow the other. I chose it to save RAM on one laptop and in CI. In production I would give each service its own instance.

- Code: `ConnectionStrings__InventoryDb` in `docker-compose.override.yml`, and `Inventory.Api.csproj` with no `ProjectReference` to the monolith.

## 2. Security

*(Monday's topic: questions come on Friday with the gateway.)*

## 3. Testing + message transformation

*(Wednesday onwards.)*

## 4. Build and deploy

### What happens on a clean `docker compose up` without `condition: service_healthy`? (17/11)

Docker starts `inventory-api` as soon as the SQL container is *started*, not when it can answer queries, which takes about 10–20 seconds. `Database.MigrateAsync()` in `Program.cs` then can't connect, throws a `SqlException`, and because nothing catches it during startup, the process crashes and the container exits.

The healthcheck runs `SELECT 1` with `sqlcmd` every 10 seconds, and `depends_on: condition: service_healthy` waits until it succeeds. Another option is retry in the code (`EnableRetryOnFailure`, which the error message itself suggests). The healthcheck fixes it at the deployment level, so the service starts only when its dependency is ready.

- Proof: with `sqlserver` stopped, `docker compose restart inventory-api` gives `Unhandled exception ... SqlException ... at Program.cs:line 18` and `Exited`.

## 5. Message channels

*(Wednesday onwards: `eshop.events`, `inventory.order-created`.)*

## 6. Domain decomposition + message routing

### `StockItem.CatalogItemId` points to a catalog product, but there is no foreign key. Why not, and what can go wrong? (17/11)

A foreign key only works **inside one database**, and the catalog lives in the monolith's `CatalogDb`, not in `InventoryDb`. Also, Inventory is its own bounded context: it only needs to know *which* product and *how many*. It doesn't need names, prices or pictures, so it stores the ID as a plain reference.

The price is that the database no longer protects consistency between the two contexts, so the application has to. Example: an admin deletes ".NET Black & White Mug" (id 2) or creates a new item 13 in the catalog. Inventory still has a stock row for 2, and has **no** row for 13, so `GET /api/inventory/13` returns `404`. In my project the seed copies ids 1–12 by hand. A real fix would be a `catalog.item-created` / `catalog.item-deleted` event that Inventory listens to (eventual consistency).
