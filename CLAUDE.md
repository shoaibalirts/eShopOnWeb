# CLAUDE.md

Semester project: I migrate eShopOnWeb towards microservices with the Strangler Pattern. I work alone, and I submit on **Sunday 22/11-2026**.

## Working rule

Changed on 18/11-2026: Claude builds the rest and drafts the report under my direction. I must be able to defend everything at the oral exam.

- **Claude builds** the code day by day, following `architecture/PLAN.md` (Wed–Sat).
- **After each working step:** commit with a short, normal message, tell me exactly what to run and what I should see, then **wait for my confirmation** before the next step.
- **Report:** Claude drafts each section in `docs/report/` as the related code is finished: first person, simple student English, specific to my files and classes, marked **DRAFT**. I review and edit.
- **Study notes:** each day Claude writes `docs/study/day-N.md`: what was built, the patterns, the key code lines with plain explanations, and 5 exam questions with ideal answers, grouped by the 6 exam topics.
- **Each evening:** Claude quizzes me with that day's 5 questions and grades my answers.
- **Never cut a requirement** without asking me first.

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
