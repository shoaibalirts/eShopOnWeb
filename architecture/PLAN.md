# Semester project plan: eShopOnWeb → microservices (Strangler Pattern)

I work alone, and my deadline is **23/11-2026**. That gives me 24 days and about 130 hours from 30/10-2026. This is the smallest plan I can find that meets every criterion in [faerdigt-produkt.md](../docs/faerdigt-produkt.md).

- **Requirements:** [opgavebeskrivelse.pdf](../docs/opgavebeskrivelse.pdf). Requirement numbers 1–9 below refer to this list and to `faerdigt-produkt.md`.
- **Architecture:** [02-target-architecture.md](02-target-architecture.md) is the single source of truth for services, extraction order and event names.
- **Baseline:** [01-monolith-flows.md](01-monolith-flows.md).

## Scope

| Area | Choice |
|---|---|
| **Services** | Catalog, Basket and Ordering (extracted from the monolith) plus Inventory (new), each with its own SQL Server database |
| **Stays in the monolith** | Pages, sign-in, user management, JWT tokens (`ITokenClaimsService`) |
| **API gateway** | YARP with JWT authentication, .NET rate limiting, and 2 Catalog replicas for load balancing |
| **Messaging** | MassTransit v8 with RabbitMQ (v9 needs a commercial license) |
| **Resilience** | Polly circuit breaker on every HTTP call between services |
| **Logging** | Serilog with Seq |
| **CI/CD** | GitHub Actions with Docker builds |

## Estimates and schedule

All values are hours for one developer. **O** = optimistic, **M** = most likely, **P** = pessimistic, and **E = (O + 4M + P) / 6**, rounded to one decimal. **Req** is the requirement number the task satisfies. Totals add up the values shown.

### Week 1 (30/10–5/11)

Gateway, shared setup and Catalog. Tag: `strangler-0-gateway`, then `strangler-1-catalog`.

| ID | Task | Req | O | M | P | E |
|---|---|---|---:|---:|---:|---:|
| G1 | Create `src/Gateway` (YARP) with a catch-all route to the monolith, fix cookies and forwarded headers, and run `tests/EndToEndTests` through it | 2, 4 | 2 | 4 | 8 | 4.3 |
| S1 | Add RabbitMQ, Seq and the new databases to compose, and make a service template with a `Dockerfile` | 5, 6, 8 | 2 | 3 | 6 | 3.3 |
| S2 | Add MassTransit v8 and `src/BuildingBlocks/Contracts` with the 4 integration events | 5 | 2 | 3 | 5 | 3.2 |
| S3 | Send Serilog logs to Seq with a `CorrelationId`, and add `/health` to the template | 7 | 1 | 2 | 4 | 2.2 |
| S4 | Extend `dotnetcore.yml` to build, test and make a Docker image for each service | 8 | 2 | 3 | 6 | 3.3 |
| C1 | Create the Catalog service with `CatalogDb`, reusing the entities and `CatalogContextSeed` | 1, 6 | 2 | 4 | 7 | 4.2 |
| C2 | Move catalog read and admin endpoints from `PublicApi` | 1, 2 | 2 | 4 | 7 | 4.2 |
| C3 | Switch monolith catalog, basket and checkout product reads to the Catalog API, with a Polly circuit breaker and fallback | 2, 6, 7 | 3 | 6 | 10 | 6.2 |
| C4 | Add gateway routes for Catalog and point BlazorAdmin at the gateway | 2, 4 | 1 | 2 | 5 | 2.3 |
| C5 | Catalog tests | 1, 8 | 1 | 2 | 4 | 2.2 |
| | **Total: Week 1 (30/10–5/11)** | | **18** | **33** | **62** | **35.4** |

### Week 2 (6/11–12/11)

Basket, gateway security, and the start of Inventory. Tag: `strangler-2-basket`.

| ID | Task | Req | O | M | P | E |
|---|---|---|---:|---:|---:|---:|
| B1 | Create the Basket service with `BasketDb` | 1, 6 | 2 | 3 | 5 | 3.2 |
| B2 | REST API for signed-in and anonymous buyers, including merge at sign-in | 1, 2 | 3 | 5 | 8 | 5.2 |
| B3 | Catalog HTTP client with a Polly circuit breaker | 6, 7 | 1 | 2 | 3 | 2.0 |
| B4 | Subscribe to `OrderConfirmedIntegrationEvent` and empty the basket | 5 | 1 | 2 | 3 | 2.0 |
| B5 | Switch monolith basket pages, sign-in merge and the header item count to the API | 2 | 3 | 5 | 8 | 5.2 |
| B6 | Basket tests | 1, 8 | 1 | 2 | 4 | 2.2 |
| A1 | Validate JWT at the gateway on protected routes, with `Web` sending a token from `ITokenClaimsService` | 4 | 2 | 3 | 6 | 3.3 |
| A2 | Add .NET rate limiting (100 requests per 10 seconds) and the `429` check in `scripts/test-gateway.ps1` | 4 | 1 | 2 | 3 | 2.0 |
| A3 | Run 2 Catalog replicas with YARP load balancing, and add the check to the script | 4 | 1 | 2 | 4 | 2.2 |
| I1 | Create the Inventory service with `InventoryDb` and seeded stock | 1, 3, 6 | 2 | 3 | 5 | 3.2 |
| I2 | Reservation logic that can't oversell, reserving all lines or none | 3 | 2 | 4 | 7 | 4.2 |
| | **Total: Week 2 (6/11–12/11)** | | **19** | **33** | **56** | **34.7** |

### Week 3 (13/11–19/11)

Finish Inventory, move Ordering, and finish the pipeline. Tags: `strangler-3-inventory`, then `strangler-4-ordering`.

| ID | Task | Req | O | M | P | E |
|---|---|---|---:|---:|---:|---:|
| I3 | Subscribe to `OrderStartedIntegrationEvent` and publish stock events, ignoring duplicate messages | 3, 5 | 2 | 3 | 6 | 3.3 |
| I4 | `GET /api/inventory/{itemId}` and a gateway route | 2, 3 | 1 | 1 | 2 | 1.2 |
| I5 | Inventory tests, including `Reserve_more_than_stock_is_rejected` | 3, 8 | 2 | 3 | 4 | 3.0 |
| O1 | Create the Ordering service with `OrderingDb` | 1, 6 | 2 | 4 | 6 | 4.0 |
| O2 | Checkout and order history API, reading Basket and Catalog over HTTP with circuit breakers | 1, 6, 7 | 3 | 4 | 7 | 4.3 |
| O3 | Order status `Pending`, `Confirmed` or `Cancelled`, driven by events, with the confirmation e-mail | 5 | 2 | 4 | 8 | 4.3 |
| O4 | Switch monolith checkout and order pages to the API | 2 | 3 | 5 | 9 | 5.3 |
| O5 | Ordering tests and `scripts/test-messaging.ps1` | 5, 8 | 2 | 4 | 7 | 4.2 |
| O6 | Remove catalog, basket and order tables and code from the monolith | 2, 6 | 1 | 1 | 3 | 1.3 |
| S5 | Push 5 images to GHCR, and add a deploy job that starts `docker-compose.deploy.yml` and checks `/health` | 8 | 2 | 3 | 6 | 3.3 |
| | **Total: Week 3 (13/11–19/11)** | | **20** | **32** | **58** | **34.2** |

### Final (20/11–23/11)

Documentation and proof.

| ID | Task | Req | O | M | P | E |
|---|---|---|---:|---:|---:|---:|
| D1 | `README.md`: decisions, boundaries, APIs, how to run, and Inventory with a sequence diagram | 1, 9 | 2 | 4 | 8 | 4.3 |
| D2 | Swagger on all 4 services | 9 | 1 | 1 | 2 | 1.2 |
| D3 | Run every check in `faerdigt-produkt.md`, fix what fails, and prepare the demo | 1–9 | 2 | 3 | 5 | 3.2 |
| | **Total: Final (20/11–23/11)** | | **5** | **8** | **15** | **8.7** |

## Totals

| Group | O | M | P | E |
|---|---:|---:|---:|---:|
| Catalog | 9 | 18 | 33 | 19.1 |
| Basket | 11 | 19 | 31 | 19.8 |
| Ordering | 13 | 22 | 40 | 23.4 |
| Inventory | 9 | 14 | 24 | 14.9 |
| Gateway | 6 | 11 | 21 | 11.8 |
| Shared setup | 5 | 8 | 15 | 8.7 |
| CI/CD | 4 | 6 | 12 | 6.6 |
| Documentation | 5 | 8 | 15 | 8.7 |
| **Total expected** | **62** | **106** | **191** | **113.0** |
| Buffer (15%) | | | | 17.0 |
| **Total with buffer** | | | | **130.0** |

**New total: 113.0 hours expected + 17.0 hours buffer (15%) = 130.0 hours.**

### Hours per week

Capacity is 130 hours spread evenly over the 24 days, about 5.4 hours a day.

| Period | Capacity | E | Spare |
|---|---:|---:|---:|
| Week 1 (30/10–5/11) | 37.9 | 35.4 | 2.5 |
| Week 2 (6/11–12/11) | 37.9 | 34.7 | 3.2 |
| Week 3 (13/11–19/11) | 37.9 | 34.2 | 3.7 |
| Final (20/11–23/11) | 21.7 | 8.7 | 13.0 |
| **Total** | **130.0** | **113.0** | **17.0** |

Most of the spare time is in the final days on purpose, so a late week can spill over without moving the deadline.

### How tight this is

- **The buffer is thin.** If every task hit its pessimistic value, the work would take 191 hours, far beyond the 130 I have. The plan only works if I stick to the scope below.
- **Checkpoints:** by 5/11, Catalog runs behind the gateway. By 12/11, Basket and gateway security are finished. If I miss a checkpoint by more than a day, I use the fallback below straight away.
- **Fallback:** I keep Basket in the monolith and let Ordering read it through an API there, which saves about 20 hours. That still leaves Catalog, Ordering and Inventory, which meets "at least 3 microservices" only if Inventory counts as one of the 3. I need to check that with my instructor.

## Later / out of scope

None of these are needed to meet the criteria, so I don't start them before everything above is finished.

| Item | Why it can wait |
|---|---|
| Payment service and its events | Payment is only an example in the assignment. The shop still takes no payments. |
| Identity as its own service | The assignment doesn't require it, and moving sign-in is one of the riskiest tasks. |
| Prometheus + Grafana | Seq, `/health` and the circuit breaker cover requirement 7. The assignment names them only as examples. |
| WebStatus and Webhooks API | No requirement needs them. |
| Outbox pattern | Without it, a crash between saving and publishing can lose an event. I accept this and document it in the README. |
| `OrderCancelledIntegrationEvent`, `ProductCreatedIntegrationEvent`, `ProductPriceChangedIntegrationEvent` | Nothing would consume the first, because a rejected order has nothing to release. Stock is seeded instead of created from events, and basket prices stay fixed when the item is added, as they do today. |
| Admin restock API and stock for new products | I seed stock for the existing products. A product with no stock record is rejected. |
| Shipping address form | Orders keep today's hard-coded test address. |
| Moving the JWT key out of source code | `AuthorizationConstants.JWT_SECRET_KEY` stays as it is, which is only acceptable for a demo. |
| Deployment to a real server or Azure | Automatic deployment runs on the CI runner, using the images from GHCR. |
| Separate ADR files, and load tests beyond the gateway script | Decisions go in the README, as the assignment allows. |

## Open questions for my instructor

1. **Inventory:** the assignment describes it as already prepared ("det forberedte lagerstyringssystem"), but I have none, so I build it from scratch.
2. **Deployment:** is deploying on the CI runner enough for "automatisk deployment" in requirement 8?
3. **Service count:** does Inventory count towards the minimum of 3 microservices? This only matters if I need the fallback.
