> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Inventory domain model and unit tests

## The rules

Inventory has to follow three rules when it reserves stock for an order:

1. **Never below zero.** A product can never have negative stock.
2. **All or none.** If one line of an order can't be reserved, no line is reserved.
3. **Once per order.** If the same order arrives twice, stock is reserved only once.

## How the code enforces them

**Never below zero** lives in `StockItem` (`src/Inventory.Api/Domain/StockItem.cs`). `Quantity` has a private setter, so the only way to lower it is the method `Reserve`. `Reserve` first calls `CanReserve`, which only allows a positive amount that is not more than the current quantity. If the check fails, `Reserve` throws an exception and nothing changes. This is the DDD idea of an aggregate that protects its own invariant.

**All or none** lives in `StockReservationService.ReserveAsync` (`src/Inventory.Api/Services/`). It first adds up the lines per product, because the same product can appear on two lines. Then `FindRejectReason` checks every line. Only if all lines fit does it call `Reserve` on each `StockItem`. Finally one `SaveChangesAsync` saves the stock changes and the reservation together. EF Core runs one `SaveChanges` as one SQL transaction, so a crash can't leave half an order reserved.

**Once per order** uses a new entity, `Reservation` (`src/Inventory.Api/Domain/Reservation.cs`). It has the `OrderId` as primary key, a `Status` (`Reserved` or `Rejected`) and a `Reason`. Before doing anything, the service looks for a reservation with the same `OrderId`. If it finds one, it returns it and changes no stock. This is the Idempotent Receiver pattern. I also save rejected orders. Otherwise a duplicate of a rejected order could be accepted later, after a restock, and the same order would get two different answers.

The primary key is the real guarantee. If two Inventory replicas get the same order at the same moment, both could pass the check, but SQL Server only allows one row per `OrderId`. The second save fails, and the message is retried.

The table was added with the migration `20261118085008_AddReservations`.

## Unit tests

The tests are in `tests/Inventory.Tests` (xUnit). There are 11 tests, and all pass.

| Test class | Tests | What they show |
|---|---|---|
| `StockItemReserve` | 5 | Stock goes down; the last unit can be taken; more than the stock throws and keeps the quantity; 0 and -1 are refused |
| `StockReservationServiceReserve` | 6 | A full order is reserved; one short line rejects the whole order and changes no stock; unknown products and empty orders are rejected; the same order twice reserves once; two lines for the same product are added up |

The service tests use the EF Core InMemory provider, with a new database for each test. This makes them fast and needs no SQL Server.

To check that the tests really protect the rules, I changed `CanReserve` to allow one unit more than the stock (`quantity <= Quantity + 1`). The tests failed, so they catch overselling. Then I changed it back.

## Limitations

The InMemory provider has no `rowversion` and no real transactions. My tests prove the rules, but not what happens when two orders hit the database at the same time. I planned a "last unit" concurrency test against SQL Server, but I cut it on 17/11 to meet the deadline. It is future work, for example with a `WebApplicationFactory` test and a SQL Server test container.
