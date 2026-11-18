> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Resilience and monitoring

With two services instead of one, there are new ways to fail: Inventory can be down, slow or overloaded while the shop is running. I want the shop to keep working, and I want to find out what happened afterwards. This section covers both.

## The catalog page and the circuit breaker

The catalog page now shows "In stock: 24", "Out of stock" or "Availability unknown" under each product. `IndexModel` (`src/Web/Pages/Index.cshtml.cs`) asks `InventoryAvailabilityService` for each item, through the gateway (`GET /api/inventory/{id}`). The 10 items on a page are asked in parallel with `Task.WhenAll`. When `StockCheck:Enabled` is off, the page doesn't call Inventory at all, so the rollback switch covers this too.

This is a synchronous call, so the page waits for it. Without protection, a slow Inventory would make the shop slow, and a dead Inventory would make every page load wait for timeouts. That is how a failure spreads from one service to another (a cascading failure).

`AddInventoryAvailability` in `src/Web/Services/InventoryAvailabilityService.cs` adds a Polly pipeline with two strategies:

- **Timeout 2 seconds** (inner): no single call can hold the page for more than 2 seconds.
- **Circuit breaker** (outer): if at least half of the calls fail, out of at least 5 calls in 30 seconds, the circuit opens. For the next 30 seconds every call fails at once with `BrokenCircuitException`, without using the network. After that, one test call is let through (half-open). If it works, the circuit closes again.

The service catches `HttpRequestException`, `TimeoutRejectedException` and `BrokenCircuitException` and returns "Availability unknown". That is the fallback: the customer can still see the products and buy them, only the stock line is missing.

eShopOnWeb's Aspire service defaults already add a standard resilience handler to every `HttpClient`, including retries. I remove it with `RemoveAllResilienceHandlers()` and add only my own pipeline, so I know exactly what happens. Retries would make a dead Inventory even slower to give up on.

### How I tested it

`tests/UnitTests/Web/Services/InventoryAvailability.cs` uses the real registration with a fake network handler that counts calls. The key test, `OpensTheCircuitAndStopsCallingInventory`, sends 15 calls to a failing Inventory. Only 5 reach the network, and the other 10 are answered by the open circuit.

Live in Docker, with Inventory running, the page loaded in 0.08 s with real stock numbers. Then I stopped both Inventory replicas:

| Page load | Time | What happened |
|---|---|---|
| 1 | 2.07 s | 10 × timeout, "Availability unknown" |
| 2 | 2.03 s | timeouts, then the log says `OnCircuitOpened` |
| 3 | 0.03 s | 10 × `BrokenCircuitException`, "Availability unknown" |

From the third load the shop is as fast as without Inventory. That is the point of the circuit breaker: fail fast instead of waiting.

## Failing messages: retry limit and dead letters

The asynchronous side has its own error handling, described in Message channels. In short, a poison message, or a message that still fails after 3 retries, goes through the dead-letter exchange `eshop.dlx` to `inventory.order-created.dead`. It is not lost, and it doesn't loop forever.

## Health checks

Inventory has `/health` with a database check (`AddDbContextCheck`). It is used in two places. Docker Compose uses a port check so replica 2 waits for replica 1, and the gateway calls `/health` every 5 seconds to take a dead replica out of the round-robin. The gateway has its own `/health` too.

## Central logs with Seq

All three applications log with Serilog to the console and to Seq (`http://localhost:5341`). Every log line gets two properties:

- `Application`: `gateway`, `eshop-web` or `inventory-api`.
- `Instance`: the host name, so I can tell `inventory-1` from `inventory-2`.

`UseSerilogRequestLogging()` writes one line per HTTP request with the path, the status code and the time.

### Following one order through three services

I use two IDs.

**`CorrelationId` = OrderId** is the business ID. `IntegrationEventPublisher`, `OrderCreatedConsumer` and `StockResultConsumer` push it into Serilog's `LogContext`, so every log line about the order has it. It is also the `CorrelationId` property on the RabbitMQ messages.

**`TraceId`** is the technical ID. ASP.NET Core already reads and writes the W3C `traceparent` header on HTTP calls, so the gateway and the monolith share a `TraceId` without any code. RabbitMQ messages have no such header, so I add it myself. `TraceContext.Inject` (in both `src/Web/Messaging/` and `src/Inventory.Api/Messaging/`) copies `Activity.Current.Id` into the message headers when publishing. `TraceContext.StartConsumer` starts a new `Activity` with that parent when consuming. Serilog takes the `TraceId` from the current `Activity`.

The result, for order 8: the Seq filter `CorrelationId = '8'` shows "Published order.created for order 8" (eshop-web), "Order 8: Reserved" (inventory-api) and "Order 8 is now Confirmed" (eshop-web). All three have the same `TraceId`. Filtering on that `TraceId` also shows the gateway's request line for `POST /Basket/Checkout`. So one search shows one order's whole path through all three services and the queue.

## Weaknesses

- **Seq is a single container without a volume.** Logs are gone after `docker compose down`, and there is no authentication. That is fine for a demo, but not for production.
- **Logs, not traces.** I propagate the `TraceId`, but I don't send spans to a tracing tool like Jaeger, so I can't see a timeline of how long each step took. OpenTelemetry would add that.
- **The circuit breaker is per monolith instance.** With several monolith instances, each one has to find out on its own that Inventory is down.
- **The fallback hides the problem from the customer.** "Availability unknown" keeps the shop running, but the order can still be rejected later by the saga. That is acceptable because the saga, not the page, makes the final decision.
- **No alerts.** Nobody is told when the circuit opens or a message is dead-lettered. Seq can send alerts, but I haven't set any up.
