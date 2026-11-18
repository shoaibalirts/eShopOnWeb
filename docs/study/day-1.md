# Day 1 – Wednesday 18/11: reservation rules and the Inventory consumer

## What was built

| Commit | What |
|---|---|
| `560c84a` | Reservation rules in `StockItem` and `StockReservationService`, the `Reservations` table, 11 unit tests |
| `6fd09d6` | `OrderCreatedConsumer`: RabbitMQ topology, manual ack, `stock.reserved` / `stock.rejected` with `CorrelationId` |

The flow inside Inventory after today:

```
order.created  →  queue inventory.order-created  →  OrderCreatedConsumer.HandleAsync
   → StockReservationService.ReserveAsync  (duplicate? → all lines fit? → reserve → one SaveChanges)
   → publish stock.reserved / stock.rejected  (CorrelationId = OrderId)
   → BasicAck
```

## Patterns

| Pattern | Where | In one sentence |
|---|---|---|
| Aggregate / invariant | `StockItem.Reserve` | Stock can only go down through one method, and that method refuses to go below zero. |
| All or none (one transaction) | `StockReservationService.ReserveAsync` | Check every line first, then save all stock changes and the reservation in one `SaveChanges`. |
| Idempotent Receiver | `Reservation` with `OrderId` as primary key | The same order arriving twice gets the same answer and changes stock only once. |
| Publish-Subscribe Channel | Topic exchange `eshop.events` | The sender publishes to the exchange and doesn't know who listens. |
| Point-to-Point Channel | Queue `inventory.order-created` | Each message is handled by exactly one consumer. |
| Message Router (topic) | Bindings `order.created` and `stock.*` | The exchange routes by routing key; `*` matches one word. |
| Correlation Identifier | `CorrelationId = OrderId` | The reply says which order it belongs to. |
| Guaranteed delivery (at-least-once) | Durable queue, persistent messages, manual ack, publisher confirms | A message is only removed after the work is done, so it may come twice but is never lost. |
| Message Translator (contract) | `OrderCreatedMessage` | Only `OrderId` and lines cross the boundary, not the monolith's `Order` entity. |

## Key code lines

**`src/Inventory.Api/Domain/StockItem.cs`**
```csharp
public bool CanReserve(int quantity) => quantity > 0 && quantity <= Quantity;
```
A reservation is allowed only for a positive amount that is not more than what's on the shelf. This one line is the "never below zero" rule. I proved the tests guard it by changing it to `Quantity + 1`: the tests failed.

**`src/Inventory.Api/Services/StockReservationService.cs`**
```csharp
var existing = await db.Reservations.FindAsync([order.OrderId], cancellationToken);
if (existing is not null) return existing;
```
Have I seen this order before? Then return the old answer and do nothing else. This is the idempotent receiver.

```csharp
var reason = FindRejectReason(wanted, stock);
if (reason is null) { foreach (...) stock[catalogItemId].Reserve(quantity); }
```
First check **all** lines. Only if none fails do I call `Reserve`. Checking first is what makes it "all or none".

```csharp
await db.SaveChangesAsync(cancellationToken);
```
One call means one SQL transaction: the stock changes and the `Reservation` row are saved together or not at all.

**`src/Inventory.Api/Messaging/OrderCreatedConsumer.cs`**
```csharp
await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, ...);
await _channel.QueueDeclareAsync(Queue, durable: true, ...);
await _channel.QueueBindAsync(Queue, Exchange, OrderCreatedKey, ...);
```
Create the post office (`eshop.events`), Inventory's mailbox (`inventory.order-created`), and the rule "put `order.created` letters in this mailbox". `durable` means they survive a RabbitMQ restart.

```csharp
await _channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
```
`autoAck: false` is manual ack. RabbitMQ keeps the message until I explicitly say it's done.

```csharp
reservation = await service.ReserveAsync(order, stoppingToken);
await PublishResultAsync(channel, reservation, stoppingToken);
await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
```
The order matters: do the work, send the answer, and **then** ack. A crash before the ack means RabbitMQ delivers again, which is safe because of idempotency.

```csharp
await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ...); // poison
await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ...);  // temporary error
```
Bad JSON never gets better, so it is dropped (the dead-letter exchange was cut). A database error might get better, so the message is put back after 2 seconds.

```csharp
CorrelationId = reservation.OrderId.ToString(),
```
The answer carries the order Id, so the monolith knows which order to confirm or cancel.

## Exam questions with ideal answers

### Microservices vs monolith

**1. Why does checkout send `order.created` as a message instead of calling Inventory over HTTP?**
With HTTP, checkout would have to wait for Inventory and would fail whenever Inventory is down. That is temporal coupling. With a message, the monolith saves the order as `Pending`, publishes, and is done. If Inventory is down, the message waits in the durable queue `inventory.order-created` and is handled when Inventory is back. The price is eventual consistency: the order is `Pending` for a moment instead of being confirmed at once.

### Security

*(No questions today: the gateway and JWT come on Friday.)*

### Testing + message transformation

**2. How do you know the all-or-none rule works, and what do your tests *not* prove?**
`RejectsWholeOrderAndChangesNoStockWhenOneLineIsShort` orders 4 of item 1 (fits) and 5 of item 2 (only 3). It checks that the order is rejected and that item 1 still has 10. I also broke the rule on purpose (`Quantity + 1`) and the tests failed. The tests use the EF InMemory provider, which has no `rowversion` and no real transactions, so they don't prove the concurrency protection. That test was cut and is future work.

### Build and deploy

*(No new questions today; see `docs/exam-notes.md` for the healthcheck.)*

### Message channels

**3. What is the difference between `eshop.events` and `inventory.order-created`, and what happens to orders while Inventory is down?**
`eshop.events` is an exchange: it doesn't store anything, it only routes each message to the queues bound to it. `inventory.order-created` is a queue: it stores messages until a consumer acks them. Because the queue is durable and the messages are persistent, orders placed while Inventory is down wait in the queue and are handled when Inventory starts again.

**4. Why do you ack after publishing the result, and what does at-least-once mean here?**
If I acked first and then crashed, the order would be gone and never get an answer. Acking last means a crash makes RabbitMQ deliver the message again. So a message can be handled twice but never lost: that's at-least-once. It's safe because `ReserveAsync` is idempotent. The `Reservation` row with the same `OrderId` makes the second run return the first answer without touching stock.

### Domain decomposition + message routing

**5. How does the topic exchange route `stock.reserved` and `stock.rejected` to the monolith, and what if a new Shipping service also needs `order.created`?**
The monolith's queue is bound with the pattern `stock.*`, where `*` matches exactly one word, so both keys reach it with one binding. For Shipping I would add a new queue, for example `shipping.order-created`, bound to `order.created`. The exchange then copies each `order.created` into both queues. Neither the monolith nor Inventory changes, which is the point of publish-subscribe.
