# Day 3 – Friday's plan, done on Wednesday 18/11: gateway, security, resilience, monitoring

## What was built

| Commit | What |
|---|---|
| `e68e3b7` | YARP gateway `src/Gateway`: catch-all → monolith, `/api/inventory/*` → Inventory. Only the gateway has a host port (5000). |
| `d192610` | JWT validation at the gateway on `PUT /api/inventory/{itemId}/stock`, with the monolith's key and the role `Administrators`. 6 tests. |
| `cea3728` | `AddRateLimiter`: 100 requests per 10 s per IP on the inventory routes, then 429 + `Retry-After`. 3 tests. |
| `c073267` | Serilog → Seq in all three apps. `CorrelationId` = OrderId, and `traceparent` in RabbitMQ headers, so one `TraceId` covers all three services. |
| `e13ed92` | Catalog page shows stock. Polly timeout (2 s) + circuit breaker, with the fallback "Availability unknown". 4 tests. |
| `bc67fc8` | 2 Inventory replicas, YARP round-robin with active health checks, Competing Consumers on the queue. |
| `6467d24` | Dead-letter exchange `eshop.dlx` → `inventory.order-created.dead`, and a retry limit of 3 (`x-retry-count`). 4 contract tests. |

The request path now:

```
Browser → gateway :5000
  /api/inventory/{id}/stock PUT → rate limit → JWT + role Administrators → inventory-1 or inventory-2
  /api/inventory/** GET         → rate limit → inventory-1 or inventory-2 (round-robin, /health checked)
  everything else               → monolith (Host header kept)
Monolith catalog page → gateway → Inventory   (Polly: timeout 2 s, circuit breaker, fallback)
```

## Patterns

| Pattern | Where | In one sentence |
|---|---|---|
| Strangler Fig facade | `src/Gateway/appsettings.json` routes | A proxy in front of the monolith sends one route at a time to the new service. |
| API Gateway | `src/Gateway` | One entry point that also handles cross-cutting concerns: tokens, rate limits, load balancing. |
| Edge authentication | `AddJwtBearer` + policy `admin` | The token is checked once at the edge, so the service behind it stays simple. |
| Rate limiting | `AddRateLimiter`, fixed window per IP | Stops one client from overloading the service. |
| Circuit breaker + fallback | `AddInventoryAvailability` | Fail fast when a dependency is down, and show a safe default instead. |
| Load balancer + health check | cluster `inventory`, `RoundRobin`, `HealthCheck.Active` | Spread calls over replicas and skip the dead ones. |
| Competing Consumers | 2 replicas on `inventory.order-created` | Several consumers on one queue; each message goes to one of them. |
| Correlation Identifier / distributed tracing | `LogContext.PushProperty("CorrelationId")`, `TraceContext` | One ID follows a request across services and the queue. |
| Dead Letter Channel + retry limit | `eshop.dlx`, `x-retry-count` | Messages that can't be handled are parked instead of lost or looping. |

## Key code lines

**`src/Gateway/appsettings.json`**
```json
"monolith": { "ClusterId": "monolith", "Order": 1000, "Match": { "Path": "{**catch-all}" } }
```
The catch-all takes everything that no other route takes. A lower `Order` wins, so the Inventory routes (Order 100 and the default 0) come first. To strangle the next piece I add another route.

```json
"inventory": { ..., "Match": { "Path": "/api/inventory/{**rest}", "Methods": [ "GET" ] } }
```
Only `GET`. Otherwise a `POST` to `/api/inventory/1/stock` would skip the protected `PUT` route and reach Inventory without a token.

**`src/Gateway/Program.cs`**
```csharp
options.MapInboundClaims = false;
```
Without this, the handler renames `role` to `http://schemas.microsoft.com/.../role`, the policy can't find the role, and even the admin gets 403.

```csharp
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
```
Rate limiting first: a flood is stopped before the gateway spends CPU on checking signatures.

**`src/Web/Services/InventoryAvailabilityService.cs`**
```csharp
pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions { FailureRatio = 0.5, MinimumThroughput = 5, ... });
pipeline.AddTimeout(TimeSpan.FromSeconds(2));
```
The first strategy added is the outer one. The timeout is inside, so a timeout counts as a failure for the circuit breaker.

```csharp
catch (Exception ex) when (ex is HttpRequestException or BrokenCircuitException or TimeoutRejectedException)
{ return Unknown; }
```
The fallback. The page always renders.

**`src/Inventory.Api/Messaging/TraceContext.cs`**
```csharp
properties.Headers[Header] = Activity.Current.Id;            // publish: "00-<traceId>-<spanId>-01"
activity.SetParentId(Encoding.UTF8.GetString(bytes));        // consume: continue the same trace
```
HTTP carries `traceparent` by itself, but RabbitMQ doesn't. Header values arrive as `byte[]`, so I decode them.

**`src/Inventory.Api/Messaging/OrderCreatedConsumer.cs`**
```csharp
if (retries >= MaxRetries) { await channel.BasicNackAsync(..., requeue: false, ...); return; }
...
await channel.BasicPublishAsync("", Queue, mandatory: false, properties, delivery.Body, ...); // copy, x-retry-count + 1
await channel.BasicAckAsync(delivery.DeliveryTag, ...);                                      // then ack the original
```
`requeue: false` + the queue argument `x-dead-letter-exchange` = the message goes to the dead-letter queue. The copy is published (and confirmed) before the ack, so nothing can be lost.

**`docker-compose.yml`**
```yaml
inventory-api-2:
  depends_on:
    inventory-api:
      condition: service_healthy
```
Replica 2 waits for replica 1, so two EF migrations don't run at the same time.

## What I learned from the live tests

- **Seq got no logs at first.** Seq wasn't started, because nothing depended on it. I added `seq` to `depends_on`.
- **`x-delivery-limit` didn't work.** The quorum queue never counted my `nack(requeue: true)`, so a failing message looped 37 times. I count retries in my own header instead. The unit tests passed both times, and only the live test found the problem.
- **502 vs 503 in the gateway tests.** After I added health checks, the fake destination could be marked unhealthy, which turns 502 into 503 depending on timing. I turned the health checks off in `GatewayFactory`.

## Demo commands

```bash
# round-robin
for i in 1 2 3 4; do curl -s -D - -o /dev/null localhost:5000/api/inventory/1 | grep -i x-served-by; done
# 429
for i in $(seq 1 110); do curl -s -o /dev/null -w "%{http_code}\n" localhost:5000/api/inventory/1; done | sort | uniq -c
# circuit breaker: stop Inventory, reload http://localhost:5000 three times
docker compose stop inventory-api inventory-api-2
# Seq: http://localhost:5341, filter  CorrelationId = '<order id>'
```

## Exam questions with ideal answers

### Microservices vs monolith

**1. How is my gateway a Strangler Fig, and what would the next step look like?**
The gateway is the facade in front of the monolith. The route `{**catch-all}` with `Order` 1000 sends everything to the monolith, except what a more specific route takes. Today only `/api/inventory/*` goes to the new Inventory service. Customers see one address (`localhost:5000`) and don't notice which system answers. To strangle the next piece, for example the basket, I would build a Basket service and add a route `/basket/**` with a lower `Order` number. The monolith's version then simply stops getting traffic and can be deleted later. The rollback is to remove the route, plus the `StockCheck:Enabled` flag for the messaging side.

### Security

**2. How does the gateway decide that a restock request is allowed? Why there and not in Inventory?**
The route `inventory-restock` has `AuthorizationPolicy: admin`. The JWT handler checks the token's HMAC-SHA256 signature with the monolith's key and checks that it hasn't expired. The `admin` policy then needs the role `Administrators` in the `role` claim. No token or a bad token gives 401, the wrong role gives 403, and only then is the request forwarded. I check at the gateway because it is the only way in: Inventory has no host port. Inventory stays simple and never sees a token. The weakness is that there is no defence in depth. Someone already on the Docker network could call Inventory directly, and the shared symmetric key means anyone who has it can make admin tokens.

### Testing + message transformation

**3. How did I test the circuit breaker, both in a unit test and live?**
In `OpensTheCircuitAndStopsCallingInventory`, I use the real `AddInventoryAvailability` registration with a fake `HttpMessageHandler` that always returns 503 and counts calls. After 15 calls, only 5 reached the "network". The circuit opened after the minimum throughput of 5 failures, and the other 10 calls failed fast. Live, I stopped both Inventory replicas and loaded the catalog page three times. The first two loads took about 2 s (the timeouts), then the log showed `OnCircuitOpened`, and the third load took 0.03 s. Every product showed "Availability unknown", but the page worked.

### Build and deploy

**4. How do the two Inventory replicas work in Docker Compose, and what happens when one dies?**
`inventory-api` and `inventory-api-2` are two services from the same image, with the same environment through a YAML anchor. Replica 2 has `depends_on: inventory-api: condition: service_healthy`, so the two EF migrations don't race. The gateway's `inventory` cluster has both as destinations with `RoundRobin`, and the `X-Served-By` header showed them taking turns. When I stopped replica 2, a few requests got 502 until the next active health check (every 5 s) marked it down. After that, all requests went to replica 1. When I started it again, it came back automatically. On the RabbitMQ side, both replicas are consumers of the same queue, so the work is shared there too.

### Message channels

**5. What happens to an `order.created` message that keeps failing, and why didn't I use RabbitMQ's own delivery limit?**
On a temporary error the consumer waits 2 s, publishes a copy at the back of the queue with `x-retry-count` one higher, and then acks the original. After 3 retries it nacks with `requeue: false`. The queue's argument `x-dead-letter-exchange = eshop.dlx` sends the message to `inventory.order-created.dead`, where I can inspect it. Bad JSON goes there straight away. I first used a quorum queue with `x-delivery-limit: 3`, but my live test with SQL Server stopped showed that RabbitMQ 4.3 didn't count `nack(requeue: true)`, so the message looped 37 times. Counting the retries myself works with any queue type, and I verified it live: retries 1, 2 and 3 were shared by both replicas, then the message was dead-lettered. The order stays `Pending` until someone fixes it, which is a known weakness.

### Domain decomposition + message routing (bonus)

**6. How can I follow one order across the gateway, the monolith, RabbitMQ and Inventory in Seq?**
There are two IDs. `CorrelationId` is the OrderId. The publisher and both consumers push it into Serilog's `LogContext`, so all log lines about the order have it. `TraceId` comes from the W3C `traceparent`. ASP.NET Core passes it on HTTP calls by itself, and I copy it into the RabbitMQ message headers (`TraceContext.Inject`) and continue it when consuming (`TraceContext.StartConsumer`). In Seq, `CorrelationId = '8'` showed the publish, the reservation and the confirmation from two applications. The same `TraceId` also found the gateway's `POST /Basket/Checkout` line.
