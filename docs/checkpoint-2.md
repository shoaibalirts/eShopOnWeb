# Checkpoint 2 – 18/11-2026

I went through every criterion in [faerdigt-produkt.md](faerdigt-produkt.md) and tested it on 18/11. The live checks ran after `docker compose down -v` and `docker compose up -d`, so the system started clean.

## Must criteria

| # | Criterion | Status | Proof |
|---|---|---|---|
| 1 | One service with its own database and Dockerfile, the choice explained, 5 bounded contexts | ✅ | `src/Inventory.Api` with `InventoryDb` and `src/Inventory.Api/Dockerfile`. Section "Why Inventory" in `architecture/02-target-architecture.md` and `docs/report/01-why-inventory.md`. Context map in `architecture/00-business-story.md` |
| 2 | Gateway: `/api/inventory/*` → Inventory, catch-all → monolith. The flag works both ways | ✅ | Routes in `src/Gateway/appsettings.json`. With `STOCK_CHECK_ENABLED=false`, order 4 stayed `Pending`, item 6 stayed at 25, and the home page had no stock lines. With the flag on again, order 5 became `Confirmed` |
| 3 | Inventory in its own container. An order of 2 lowers stock by 2. Unit tests for the rules | ✅ | Live: item 5 went from 25 to 23 after order 3 (2 × item 5). `tests/Inventory.Tests`: `RejectsWholeOrderAndChangesNoStockWhenOneLineIsShort` (all or none), `StockItemReserve` tests (never below zero), `ReservesOnlyOnceWhenTheSameOrderArrivesTwice` |
| 4 | Restock without JWT → 401. A test shows 429. 2 replicas with round-robin | ✅ after a fix | Live: `PUT` without a token gave 401. `Gateway.Tests/RestockAuthorization` (6 tests) and `InventoryRateLimit` (3 tests). `X-Served-By` alternated between inventory-1 and inventory-2. **Fixed:** 110 quick calls gave 110 × 200, because the fixed window reset in the middle. A sliding window now gives 100 × 200 and 10 × 429 every time (commit `93180ae`) |
| 5 | RabbitMQ topic exchange, routing keys, bound queue, manual ack. Enough stock → `Confirmed`, too little → `Cancelled` | ✅ | `OrderCreatedConsumer` (`autoAck: false`), `RabbitMqTopology`. `bash scripts/smoke-test.sh`: 1 × item 1 → `Confirmed`, 4 × item 2 (3 in stock) → `Cancelled` |
| 6 | Own connection string, no `ProjectReference` to the monolith, the monolith never reads `InventoryDb` | ✅ | `Inventory.Api.csproj` has no `ProjectReference`. A search for `InventoryDb` outside `src/Inventory.Api` finds nothing |
| 7 | Circuit breaker with "availability unknown" instead of 500. Serilog → Seq from all three. All three answer `/health` | ✅ | Inventory stopped: 3 page loads returned 200 with "Availability unknown" ×10 (2.0 s, 2.0 s, then 0.02 s with the circuit open). Seq has events from `gateway`, `eshop-web` and `inventory-api`. `/health`, `/health/monolith` and `/health/inventory` all answer 200. **Fixed:** the monolith's `/health` was `Unhealthy` in Docker, because of two old checks that call outside addresses (commit `ae6ec9a`) |
| 8 | GitHub Actions: build, test, images to GHCR, deploy checks `/health`. A green run linked in the README. A failing test stops the pipeline | ✅ (failing-test demo still open) | Green run [44323361311]() on `8dfc3bc`: build-and-test passed 104 tests in 5 projects, images pushed `ghcr.io/shoaibalirts/{eshopwebmvc,gateway,inventory-api}:sha-8dfc3bc` and `:latest`, deploy ran `smoke-test.sh` on the runner (3 × health 200, order 1 `Confirmed`, order 2 `Cancelled`). The link is in `README.md`. Still open: show on a branch that a failing test skips `images` and `deploy` |
| 9 | README with how to run, APIs and decisions. Architecture diagram and saga sequence diagram. Inventory Swagger | ✅ | `README.md`. Swagger answers 200 at http://localhost:5000/api/inventory/swagger. The diagrams are updated in `architecture/02-target-architecture.md` and `architecture/diagrams/*.mmd`. PNGs exported to `architecture/diagrams/02-target-architecture.png` and `02-order-saga-sequence.png` |

## Extra evidence (Should and Could)

| Strengthens | Extra | Status | Proof |
|---|---|---|---|
| 3 | Concurrency test of the last unit (`rowversion`) | ❌ future work | Cut on 17/11. It needs a real SQL Server in the test, because the in-memory database has no rowversion |
| 5 | Dead-letter exchange with a retry limit | ✅ | `eshop.dlx` → `inventory.order-created.dead`. Live: SQL Server stopped → retry 1, 2 and 3, then dead-lettered. A poison message was also dead-lettered on 18/11 |
| 5 | Tests for duplicates and poison messages | ✅ partly | Duplicate: unit test `ReservesOnlyOnceWhenTheSameOrderArrivesTwice`. Poison: only tested live through the RabbitMQ management API |
| 5 | `scripts/test-messaging.ps1` runs the whole flow | ✅ partly | Replaced by `scripts/smoke-test.sh` (bash, also used in CI). It runs the saga but not the poison/duplicate cases |
| 6 | RabbitMQ and database passwords from environment variables | ✅ | `RabbitMq__Password` and `ConnectionStrings__InventoryDb` in `docker-compose.override.yml`. The values are still demo values in the file |
| 7 | One `CorrelationId` found in Seq in logs from all three | ✅ | `CorrelationId = '3'` → lines from `eshop-web` and `inventory-api`. The same `TraceId` also finds the gateway's request line |
| 8 | A `WebApplicationFactory` integration test of the API | ✅ | `tests/Inventory.Tests/Api/StockApi.cs` (7 tests, commit `b62c6cd`), plus `Gateway.Tests` |

## Final check from a clean clone

✅ I cloned the repository at commit `b62c6cd` into an empty folder, ran `docker compose up -d --build`, and then `bash scripts/smoke-test.sh`. Order 1 became `Confirmed` and order 2 became `Cancelled`.
