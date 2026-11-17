> **DRAFT.** AI-drafted from my code and docs on 17/11-2026. I must rewrite it in my own words before I submit.

# Data separation

## The rule

I follow the Database per Service pattern. Only Inventory may read or write its data. If another part of the system needs stock information, it must ask Inventory through its API or through messages. It may never read the tables directly.

The monolith shows why this matters. In eShopOnWeb, one database (`CatalogContext`) holds the catalog, the baskets and the orders. Any code can read any table, so a change to one table can break a feature in a different part of the shop.

## How I built it

**Its own database.** Inventory uses its own `InventoryDbContext` in `src/Inventory.Api/Data/InventoryDbContext.cs`. It has one table, `StockItems`.

**Its own connection string.** The setting `ConnectionStrings:InventoryDb` points to the database `InventoryDb`. When I run the service on my PC, it comes from `appsettings.json` (`Server=localhost,1434`). In Docker, the environment variable `ConnectionStrings__InventoryDb` in `docker-compose.override.yml` overrides it (`Server=sqlserver,1433`).

**No shared code.** `Inventory.Api.csproj` has no `ProjectReference` to `ApplicationCore`, `Infrastructure` or `Web`. Inventory cannot use the monolith's entities or its `DbContext`, even by mistake.

**It owns its schema.** The table is created by the EF Core migration `20261117191617_InitialCreate`. `Program.cs` runs `Database.MigrateAsync()` at startup, so a clean `docker compose up` creates the database without manual steps. In `docker-compose.yml`, Inventory waits for SQL Server with `condition: service_healthy`. Without this, the migration ran before SQL Server was ready, and the service crashed with a `SqlException`. I tested this by stopping SQL Server.

**Seed data.** The migration adds stock for the 12 products the monolith seeds in `CatalogContextSeed`. Each product has 25 units, except product 2 (".NET Black & White Mug"), which has 3. I chose 3 so I can show a rejected order in the demo.

**Prepared for concurrency.** `StockItem` has a `RowVersion` property mapped as a SQL Server `rowversion` column. It will stop two orders from taking the last unit at the same time.

The data model is small. A `StockItem` has a `CatalogItemId` and a `Quantity`. `CatalogItemId` is also the primary key, and I set `ValueGeneratedNever()` because the number must match the product Id in the catalog.

## Trade-off 1: no foreign key

`CatalogItemId` points to a product in the monolith's catalog, but it is not a foreign key. SQL Server cannot enforce a foreign key across two databases, and the rule above does not allow Inventory to reach into the catalog's tables anyway.

The cost is that the database no longer keeps the two contexts consistent. My code must do it. For example, if an admin adds product 13 to the catalog, Inventory has no row for it, and `GET /api/inventory/13` returns `404`. Today I copy the Ids 1–12 by hand in the seed.

**In production** I would let the catalog publish events like `catalog.item-created` and `catalog.item-deleted`, and Inventory would create or remove its stock rows when it receives them. The data would then be consistent after a short delay (eventual consistency).

## Trade-off 2: one SQL Server, separate databases

Inventory and the monolith use the same SQL Server container, `sqlserver`, but separate databases. The monolith uses `Microsoft.eShopOnWeb.CatalogDb` and `Microsoft.eShopOnWeb.Identity`, and Inventory uses `InventoryDb`. I chose this to save memory on my laptop and in the CI pipeline.

This still follows the rule, because the rule is about who owns and accesses the data, not about which server it runs on. But it has costs:

- If the SQL Server container stops, both the monolith and Inventory stop working.
- A heavy query in one database can slow down the other.
- Inventory connects as `sa`, the SQL Server admin. Technically it could read `CatalogDb`. Today the separation is kept by my code and configuration, not by database permissions.

**In production** I would give each service its own SQL Server instance (for example its own Azure SQL database), and its own login that can only access its own database. Then the separation would also be enforced by the database itself.
