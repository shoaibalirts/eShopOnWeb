# Day 2 – Thursday's plan, done on Wednesday 18/11: the monolith joins the saga (Checkpoint 1)

## What was built

| Commit | What |
|---|---|
| `018ebdc` | `Order.Status` (`Pending` / `Confirmed` / `Cancelled`), `Confirm()` / `Cancel()`, migration `AddOrderStatus`, real status in My Orders, 5 unit tests |
| `3422c70` | `IntegrationEventPublisher` (translator + publish), `RabbitMqPublisher`, `RabbitMqTopology`, `StockCheck:Enabled`, 3 contract tests |
| `9f09ae1` | `StockResultConsumer` in Web, Docker wiring, the Seq fix that lets Web start in Docker, **Checkpoint 1 passed** |

The whole saga now runs:

```
Pay Now → OrderService saves Order (Pending) → OrderCreatedEvent (Mediator, in-process)
  → IntegrationEventPublisher: flag on? → Translate → publish order.created
  → Inventory reserves → stock.reserved / stock.rejected
  → StockResultConsumer: CorrelationId → order → Confirm() / Cancel() → save → ack
  → My Orders shows Confirmed / Cancelled
```

**Checkpoint 1 result:** order 1 (item 1 × 2) became `Confirmed`, order 2 (item 2 × 4) became `Cancelled`, and order 3 with the flag off stayed `Pending`.

## Patterns

| Pattern | Where | In one sentence |
|---|---|---|
| Domain event → integration event | `OrderCreatedEvent` → `IntegrationEventPublisher` | An internal, in-process event is turned into a public message for other services. |
| Message Translator | `IntegrationEventPublisher.Translate` | The big `Order` entity becomes the small `order.created` contract (`Units` → `quantity`). |
| Contract test | `OrderCreatedTranslation.SerializesToTheContractInventoryExpects` | Fixes the exact JSON Inventory depends on. |
| Saga (choreography) | Monolith ↔ Inventory via events | A chain of local transactions connected by messages, with no coordinator. |
| State machine | `Order.Confirm()` / `Order.Cancel()` | Only `Pending` can move, so duplicates and late answers do nothing. |
| Correlation Identifier | `delivery.BasicProperties.CorrelationId` | The answer is matched to the order by the order Id in the message properties. |
| Content-based / topic routing | Binding `stock.*`, `if RoutingKey == ...` | The routing key decides both where the message goes and what the consumer does. |
| Feature toggle (Strangler rollback) | `StockCheck:Enabled` | One setting turns the new behaviour off without a deploy. |

## Key code lines

**`src/ApplicationCore/Entities/OrderAggregate/Order.cs`**
```csharp
public void Confirm()
{
    if (Status == OrderStatus.Pending) { Status = OrderStatus.Confirmed; }
}
```
Only a `Pending` order can be confirmed. A second `stock.reserved` finds it already `Confirmed` and does nothing, so the monolith's side is idempotent. A `Cancelled` order can never become `Confirmed`.

**`src/Infrastructure/Data/Migrations/20261118113254_AddOrderStatus.cs`**
```csharp
defaultValue: "Pending");
```
EF generated `""` here. I changed it so old orders get a real status.

**`src/Web/Messaging/IntegrationEventPublisher.cs`**
```csharp
public class IntegrationEventPublisher(...) : INotificationHandler<OrderCreatedEvent>
```
Mediator calls this handler whenever `OrderService` publishes `OrderCreatedEvent`. `OrderService` itself did not change at all.

```csharp
if (!stockCheck.Value.Enabled) { return; }
```
The rollback switch: with the flag off, nothing leaves the monolith.

```csharp
new(order.Id, order.OrderItems.Select(item => new OrderCreatedLine(item.ItemOrdered.CatalogItemId, item.Units)).ToList());
```
The Message Translator: keep only the order Id and what was bought.

```csharp
catch (Exception ex) { logger.LogError(ex, "... it stays Pending", ...); }
```
If RabbitMQ is down, the checkout still succeeds and the order stays `Pending`. This is the dual-write problem; the Outbox pattern is the real fix.

**`src/Web/Messaging/RabbitMqTopology.cs`**
```csharp
await channel.QueueDeclareAsync(InventoryQueue, durable: true, ...);
await channel.QueueBindAsync(InventoryQueue, Exchange, OrderCreatedKey, ...);
```
The monolith also creates Inventory's queue, so the first `order.created` is not lost if Inventory hasn't started yet. An exchange only routes; it never stores.

**`src/Web/Messaging/StockResultConsumer.cs`**
```csharp
if (!int.TryParse(delivery.BasicProperties.CorrelationId, out var orderId))
```
Correlation Identifier: which order is this answer about?

```csharp
if (delivery.RoutingKey == RabbitMqTopology.StockReservedKey) order.Confirm();
else if (delivery.RoutingKey == RabbitMqTopology.StockRejectedKey) order.Cancel();
```
The routing key is the "verb" of the message.

```csharp
logger.LogWarning("RabbitMQ is not reachable ({Error}), retrying in 5 seconds", ex.Message);
```
The monolith is the shop: if RabbitMQ is down, it keeps retrying instead of crashing.

**`src/Web/Program.cs`**
```csharp
if (builder.Configuration.GetValue<bool>("StockCheck:Enabled"))
{
    builder.Services.AddHostedService<StockResultConsumer>();
}
```
With the flag off, the consumer doesn't even start, so the monolith has no RabbitMQ dependency at all.

**`docker-compose.override.yml`**
```yaml
- StockCheck__Enabled=${STOCK_CHECK_ENABLED:-true}
```
On by default in Docker. `STOCK_CHECK_ENABLED=false docker compose up -d eshopwebmvc` turns it off for the demo.

## Before the demo

The test messages I published by hand on 18/11 (orders 1001–1004 and 2001–2002) are stored in `InventoryDb.Reservations`. Real orders start at 1, so there's no clash for now. Before the exam demo I run `docker compose down` and then `docker compose up -d`. There are no volumes, so all databases start clean.

## Exam questions with ideal answers

### Microservices vs monolith

**1. What does `StockCheck:Enabled` do, and why is it part of the Strangler Pattern?**
It is the rollback switch for the new behaviour. When it is `false`, `IntegrationEventPublisher` publishes nothing and `StockResultConsumer` isn't started, so the monolith works exactly as before the migration and doesn't need RabbitMQ or Inventory. The Strangler Pattern moves functionality step by step while the shop stays open, and every step needs a safe way back. I showed it in Docker: with the flag off, order 3 stayed `Pending` and no messages were sent.

### Security

*(No questions today: the gateway and JWT come on Friday.)*

### Testing + message transformation

**2. What does `IntegrationEventPublisher.Translate` do, and why not send the whole `Order`?**
It is a Message Translator: it turns the monolith's `Order` entity into the small `order.created` contract with only `orderId` and lines of `catalogItemId` and `quantity`. Sending the whole entity would couple Inventory to the monolith's internal model, so every change to `Order` could break Inventory. It would also leak personal data (buyer and address) that Inventory doesn't need. The contract test `SerializesToTheContractInventoryExpects` fixes the exact JSON, so a rename fails in the build instead of in production.

### Build and deploy

**3. Why did the monolith not start in Docker at first, and how did you find and fix it?**
`Web/Program.cs` calls `AddSeqEndpoint("seq")`, and that throws at startup when no Seq URL is configured. I found it in `docker compose logs eshopwebmvc`: "Unable to add a Seq health check because the 'ServerUrl' setting is missing". It was an existing problem in eShopOnWeb's Docker setup, not caused by my code. I fixed it by adding `ConnectionStrings__seq=http://seq` in `docker-compose.override.yml`, pointing at the Seq container I had added on Tuesday.

### Message channels

**4. What happens if RabbitMQ is down when a customer checks out, and what would fix it properly?**
The order is saved as `Pending` first. Then publishing `order.created` fails. I catch and log the error so the customer still gets the success page, but the order stays `Pending`, because no message was sent. This is the dual-write problem: saving to the database and publishing to the broker are two separate actions that can't share one transaction. The proper fix is a Transactional Outbox, where the message is saved in the same database transaction as the order and published by a background job later.

### Domain decomposition + message routing

**5. Your saga is "choreographed". What does that mean, and what is missing compared to a full saga?**
There is no central coordinator: each service reacts to events from the other. The monolith publishes `order.created`, Inventory answers with `stock.reserved` or `stock.rejected`, and the monolith sets `Confirmed` or `Cancelled`. Two things are missing. First, compensation: if a confirmed order is cancelled later, nothing releases the stock, so I would need an `order.cancelled` event. Second, a timeout: if Inventory never answers, the order stays `Pending` forever.
