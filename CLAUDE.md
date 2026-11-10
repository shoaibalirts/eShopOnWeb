# CLAUDE.md

Semester project: I migrate eShopOnWeb towards microservices with the Strangler Pattern. I work alone, and I submit on **Sunday 22/11-2026**.

## Working rule

**Shoaib writes the code.** Claude explains first and reviews after. Claude writes boilerplate only when I ask for it.

- **Before I code:** explain the concept and the approach, using this project's code as the example.
- **After I code:** review what I wrote: bugs, risks and how it matches the architecture.
- **Never:** write service code, tests or features unless I explicitly ask for boilerplate.

## Scope

My instructor Jakob confirmed on 6/11-2026 that I build **one** microservice, **Inventory**, beside the monolith.

- **New:** `src/Inventory.Api` with its own `InventoryDb`, and `src/Gateway` (YARP).
- **Stays in the monolith:** Catalog, Basket, Ordering, Identity, and all pages. The monolith gets an order status (`Pending` / `Confirmed` / `Cancelled`), publishes `order.created` and consumes `stock.*`.
- **Rollback:** the feature flag `StockCheck:Enabled` in the monolith.

## Sources of truth

| File | Holds |
|---|---|
| `architecture/02-target-architecture.md` | The service, the routes, the message names and the diagram |
| `architecture/PLAN.md` | The day-by-day plan, checkpoints and cut list |
| `docs/faerdigt-produkt.md` | The submission checks, one per requirement (in Danish) |
| `architecture/00-business-story.md` | Bounded contexts and the context map |
| `architecture/01-monolith-flows.md` | How the monolith works today |

Don't edit `AFLEVERING-ENGLISH.md`.

## Stack

- .NET 10, ASP.NET Core Web API, EF Core with SQL Server (one database per service)
- **RabbitMQ.Client 7**, not MassTransit: topic exchange `eshop.events`, routing keys `order.created`, `stock.reserved` and `stock.rejected`, queue `inventory.order-created`, a dead-letter exchange, manual ack, and `CorrelationId = OrderId`
- YARP gateway: JWT validation, `AddRateLimiter`, round-robin to 2 Inventory replicas
- Polly circuit breaker, Serilog → Seq, health checks
- xUnit, `WebApplicationFactory`
- Docker Compose, GitHub Actions → GHCR

## Writing docs

Write project documentation in the first person ("I").
