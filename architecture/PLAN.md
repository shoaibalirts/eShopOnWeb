# Semester project plan: Inventory beside the eShopOnWeb monolith (Strangler Pattern)

I work alone. My instructor Jakob confirmed on 6/11-2026 that I build **one** microservice, **Inventory**, beside the eShopOnWeb monolith.
I submit on **Sunday 22/11-2026**. That gives me 12 days from Tuesday 10/11 and **58 hours** of planned work.

- **Requirements:** [opgavebeskrivelse.pdf](../docs/opgavebeskrivelse.pdf). The checks I submit against are in [faerdigt-produkt.md](../docs/faerdigt-produkt.md).
- **Architecture:** [02-target-architecture.md](02-target-architecture.md) is the single source of truth for the service, the routes and the message names.
- **Baseline:** [01-monolith-flows.md](01-monolith-flows.md).

Every day has three parts:

- **Build:** I write all the code myself.
- **Learn:** at most 1 hour of video, reading or tutorial, chosen to match that day's build.
- **Report:** 20–30 minutes writing the report section about what I built that day, while it is fresh.

**E h** is the expected number of hours for the whole day.

## Scope (Must / Should / Could)

- **Must:** without it, a requirement fails or the final demo doesn't work. Only Must items are checked in [faerdigt-produkt.md](../docs/faerdigt-produkt.md).
- **Should:** it makes the system safer or the evidence stronger, but the demo works without it.
- **Could:** extra proof, only if there is time.

| Priority | Item | Why | Day |
|---|---|---|---|
| **Must** | `Inventory.Api` with its own `InventoryDb`, seeded stock, `GET /api/inventory/{itemId}`, Swagger, `/health`, `Dockerfile` | Requirements 3, 6 and 9 | Wed 11 |
| **Must** | Reservation rules: all lines or none, never below zero, one reservation per `OrderId`, with unit tests | The core of requirement 3 | Thu 12 |
| **Must** | RabbitMQ: `order.created` → `stock.reserved` / `stock.rejected` on `eshop.events`, with manual ack | Requirement 5 | Fri 13 |
| **Must** | Order status `Pending` / `Confirmed` / `Cancelled` in the monolith, shown in My Orders | The final demo | Sat 14 |
| **Must** | Gateway: catch-all → monolith, `/api/inventory/*` → Inventory, and the feature flag `StockCheck:Enabled` | Requirement 2 (Strangler, with rollback) | Sun 15 |
| **Must** | JWT on restock, rate limiting with a `429` test, 2 Inventory replicas with round-robin | Requirement 4 names all three | Mon 16, Wed 18 |
| **Must** | Polly circuit breaker on the product page with a fallback, Serilog → Seq from all three, `/health` on all three | Requirement 7 | Tue 17 |
| **Must** | GitHub Actions: build, test, image → GHCR, deploy job with a `/health` check | Requirement 8 | Wed 18 |
| **Must** | `README.md` and the two diagrams | Requirement 9 | Fri 20 |
| **Should** | Credentials from environment variables | Good security practice, and cheap | Mon 16 |
| **Should** | Dead-letter exchange with a retry limit | Real error handling, but the demo works without it | Sun 15 |
| **Should** | `rowversion` concurrency control and the "last unit" test | Protects the last unit, but is hard to show live | Thu 12, Thu 19 |
| **Should** | One `CorrelationId` searchable across all three services in Seq | Strong evidence for requirement 7, but plain logging already counts | Tue 17 |
| **Could** | Duplicate-message and poison-message tests | Idempotency itself is Must; these are extra proof | Sun 15 |
| **Could** | `WebApplicationFactory` integration test | The unit tests already cover requirement 8 | Thu 19 |
| **Could** | `scripts/test-messaging.ps1` | I can show the same flow by hand in the demo | Thu 19 |

## Week A: the messaging core (Checkpoint 1 on Saturday night)

| Day | Build (I code) | Learn (≤ 1 h) | Report (20–30 min) | E h |
|---|---|---|---|---:|
| Tue 10 eve | Re-scope the docs with Claude Code. Start RabbitMQ in Docker (`rabbitmq:4-management`) and open the management UI. | Fireship "RabbitMQ in 100 seconds"; Özkaya Design course: monolith → microservices section | – | 1.5 |
| Wed 11 | `src/Inventory.Api` (.NET 10 Web API): `StockItem` entity, EF Core with its own `InventoryDb` (SQL Server container), seeded stock for the catalog items, `GET /api/inventory/{itemId}`, Swagger, `/health`, `Dockerfile` and a compose entry | Özkaya Design: decomposition and data per service (40 min); microservices.io "Database per service" | Why Inventory (matrix + 3-sentence reason); data separation | 4 |
| Thu 12 | Domain rules: reserve all lines or none, never below zero, one reservation per `OrderId` (idempotency), `rowversion` concurrency. xUnit project `Inventory.Tests` | DDD video on aggregates and invariants (25 min) | Inventory domain model; unit testing | 4 |
| Fri 13 | Messaging in Inventory with RabbitMQ.Client 7: topic exchange `eshop.events`, queue `inventory.order-created` bound to `order.created`, a `BackgroundService` consumer with manual ack, and publishing `stock.reserved` / `stock.rejected` with `CorrelationId = OrderId`. A console publisher to test it | RabbitMQ .NET tutorials 3 and 4 (hands-on, 60 min); Kök course: exchanges and routing keys | Message channels; message routing | 4 |
| Sat 14 | Monolith: `Order.Status` (`Pending` / `Confirmed` / `Cancelled`) and a migration; an `IntegrationEventPublisher` that translates `OrderCreatedEvent` into `order.created` JSON (only `OrderId`, lines and quantities); a consumer in `Web` for `stock.*` that updates the status; My Orders shows the status. First order end to end in Docker → **Checkpoint 1** | MS eBook: integration events chapter (40 min); Richardson saga talk (50 min at 1.25×) | Message transformation; the order-placement saga with a sequence diagram | 7 |
| Sun 15 | Robustness: dead-letter exchange with a retry limit, a duplicate-message test and a poison-message test. Gateway `src/Gateway` (YARP): catch-all → monolith, `/api/inventory/*` → Inventory. Feature flag `StockCheck:Enabled` in the monolith for instant rollback | Sam Newman talk (45 min); Milan Jovanović YARP article | Error handling (idempotent receiver, DLQ); Strangler implementation; the Opgavesaga design doc, written from the real flow | 7 |
| | **Total: Week A** | | | **27.5** |

## Week B: hardening, operations, report (Checkpoint 2 on Thursday)

| Day | Build (I code) | Learn (≤ 1 h) | Report (20–30 min) | E h |
|---|---|---|---|---:|
| Mon 16 | Security: the gateway validates the monolith's JWT on `PUT /api/inventory/{itemId}/stock` (admin restock); rate limiting with `AddRateLimiter` and a `429` test; RabbitMQ and database credentials from environment variables | Lavi Security course: gateway, authentication/authorization, service-to-service (60 min) | Security (also my class presentation on Question 2) | 4 |
| Tue 17 | Resilience and monitoring: the product page asks `GET /api/inventory/{id}` through the gateway, with a Polly circuit breaker and the fallback "availability unknown"; Serilog → Seq from the monolith, the gateway and Inventory, with `CorrelationId`; health checks on all three | Bryan Hogan Polly talk (first 30 min); Microsoft "Circuit Breaker pattern" page | Resilience and monitoring | 4 |
| Wed 18 | Scale and CI/CD: 2 Inventory replicas (compose `replicas: 2` and YARP round-robin; both read the same queue, which is the Competing Consumers pattern); GitHub Actions: build, test, Docker image → GHCR, and a deploy job on the runner with a `/health` check | Schwarzmüller GitHub Actions: workflow basics and Docker (45 min) | Build and deploy; scalability | 4 |
| Thu 19 | Testing: a `WebApplicationFactory` integration test of the API; a concurrency test (two reservations for the last unit → one rejected); `scripts/test-messaging.ps1`; run every criterion in `faerdigt-produkt.md` → **Checkpoint 2** | – | Testing (integration, message contract) | 4 |
| Fri 20 | `README.md` (how to run, APIs, decisions); diagrams: the one-service target architecture and the saga sequence; Swagger | – | Assemble the report from the daily sections | 4 |
| Sat 21 | Bug fixes only | – | Evaluation, trade-offs and future work; proofread; mock exam round 1 | 6 |
| Sun 22 | Clean clone → `docker compose up` → place an order → the status changes. **Submit** | – | Mock exam round 2 | 4.5 |
| | **Total: Week B** | | | **30.5** |

**Total: 27.5 + 30.5 = 58 hours.**

## Checkpoints

| Checkpoint | When | Passed when |
|---|---|---|
| **1** | Saturday 14/11, evening | In Docker, I place an order in the shop. It starts as `Pending`, Inventory receives `order.created`, and the order becomes `Confirmed` (or `Cancelled` if there isn't enough stock). My Orders shows the status. |
| **2** | Thursday 19/11 | Every criterion in [faerdigt-produkt.md](../docs/faerdigt-produkt.md) passes. |

If I miss a checkpoint, I use the cut list straight away instead of moving the deadline.

## Cut list

If I miss a checkpoint by more than half a day, I cut from the top of this list until I'm back on plan. I cut Could items first, then Should items, and never Must items.

| Cut order | I drop | I keep | Saves (about) |
|---|---|---|---|
| 1 | `scripts/test-messaging.ps1` | The same flow shown by hand in the demo | 1 h |
| 2 | `WebApplicationFactory` integration test | The unit tests | 1 h |
| 3 | Duplicate- and poison-message tests | The idempotency rule in the code | 1.5 h |
| 4 | `CorrelationId` search across services | Plain logs from all three in Seq | 1 h |
| 5 | Dead-letter exchange | Manual ack; the DLQ is described as future work in the report | 1.5 h |
| 6 | The "last unit" concurrency test | `rowversion` in the code | 1 h |

Cutting everything saves about 7 hours, which is about one full day. Credentials from environment variables stay, because they take minutes.

**What I never cut:** the learn and report time. The report is written from the daily sections, so skipping a day's report costs more time on Friday 20/11.

## Clarified with instructor

**30/10-2026:**

1. **Inventory:** it is simply a system I build myself to track stock levels.
2. **Deployment:** deploying on the CI runner counts as "automatisk deployment" for requirement 8.

**6/11-2026:**

3. **Service count:** because I work alone, I build **one** microservice, Inventory, beside the monolith. This replaces the earlier answer that Inventory counts towards a minimum of 3.
