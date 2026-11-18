> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Message channels and routing

## Why messages

When a customer places an order, the monolith must not wait for Inventory. If checkout called Inventory over HTTP, the shop could not take orders while Inventory was down. So the monolith saves the order as `Pending` and publishes a message, and Inventory answers with another message. The two services don't need to be running at the same time.

I use RabbitMQ with the library RabbitMQ.Client 7. I chose it over MassTransit so I could see and explain every part: exchanges, queues, bindings and acks.

## The channels

| Name | Type | Role |
|---|---|---|
| `eshop.events` | Topic exchange, durable | Receives every event and routes it by routing key |
| `inventory.order-created` | Queue, durable, bound to `order.created` | Inventory's inbox |
| `monolith.stock-results` | Queue, durable, bound to `stock.*` | The monolith's inbox for the answers |

The exchange is a Publish-Subscribe Channel: the sender doesn't know who listens. Each queue is a Point-to-Point Channel: one message is handled by one consumer. When I run 2 Inventory replicas, they read from the same queue and share the work (Competing Consumers). `BasicQosAsync` with `prefetchCount: 10` stops one replica from taking all the messages.

The topic exchange routes by routing key. `order.created` goes to Inventory's queue. The monolith binds its queue with `stock.*`, where `*` matches one word, so both `stock.reserved` and `stock.rejected` arrive with one binding. A new service, for example Shipping, could get its own copy of `order.created` by binding a new queue, without any change to the monolith or Inventory.

## The messages

| Routing key | Body (JSON) | Properties |
|---|---|---|
| `order.created` | `{ "orderId": 1001, "lines": [{ "catalogItemId": 1, "quantity": 2 }] }` | – |
| `stock.reserved` | `{ "orderId": 1001 }` | `CorrelationId = "1001"` |
| `stock.rejected` | `{ "orderId": 1002, "reason": "Not enough stock for item 2: wanted 5, have 3." }` | `CorrelationId = "1002"` |

The records are `OrderCreatedMessage`, `StockReservedMessage` and `StockRejectedMessage` in `src/Inventory.Api/Messages/`. The `order.created` message only has what Inventory needs. It has no prices, buyer or address. The answers use the Correlation Identifier pattern: `CorrelationId` is the `OrderId`, so the monolith knows which order the answer belongs to. All messages are persistent (`DeliveryModes.Persistent`).

## Reliable handling in Inventory

The consumer is `OrderCreatedConsumer` (`src/Inventory.Api/Messaging/`), a `BackgroundService`. At startup it declares the exchange, the queue and the binding. Declaring is safe to repeat: RabbitMQ only creates what is missing. Start order still matters. An exchange doesn't store messages, so if the monolith published `order.created` before Inventory had created its queue, the message would be dropped. I solved this by letting the monolith declare the whole topology too. `RabbitMqTopology.DeclareAsync` in `src/Web/Messaging/` declares the exchange and both queues with their bindings before the first publish. Now it doesn't matter which service starts first.

It uses manual ack (`autoAck: false`). For each message it does three things in this order:

1. Reserve stock with `StockReservationService.ReserveAsync`.
2. Publish `stock.reserved` or `stock.rejected`. The channel uses publisher confirms, so this step waits until RabbitMQ has stored the answer.
3. Ack the `order.created` message.

If Inventory crashes before step 3, RabbitMQ delivers the message again. This means a message can be handled more than once, but it is never lost (at-least-once delivery). That is safe because `ReserveAsync` is idempotent: the second time it returns the first answer without changing stock.

There are two kinds of errors:

- **A poison message**, such as invalid JSON, can never succeed. The consumer logs an error and nacks with `requeue: false`. The queue has the argument `x-dead-letter-exchange = eshop.dlx`, so RabbitMQ moves the message to the dead-letter queue `inventory.order-created.dead` instead of deleting it. There I can inspect it in the management UI.
- **A temporary error**, such as the database being down, may succeed later. The consumer waits 2 seconds, publishes a copy of the message to the back of the queue with the header `x-retry-count` one higher, and then acks the original. After 3 retries it nacks with `requeue: false`, so the message goes to the dead-letter queue too. Without this limit, a message that always fails would loop forever.

I first tried to let RabbitMQ count the retries with a quorum queue and `x-delivery-limit`. My live test showed that RabbitMQ 4.3 did not count a `nack` with `requeue: true`, so the message looped 37 times and never reached the dead-letter queue. That is why I count the retries myself in my own header. The copy is published with publisher confirms before the ack, so a crash in between gives a duplicate, never a lost message, and `ReserveAsync` is idempotent.

## How I tested it

I published test messages to `eshop.events` with the RabbitMQ management API. To see the answers, I bound a test queue to `stock.*`.

| Order | Lines | Result |
|---|---|---|
| 1001 | item 1 × 2 | `Reserved`, item 1 went from 25 to 23 |
| 1002 | item 1 × 1, item 2 × 5 | `Rejected`, and item 1 stayed at 23 (all or none) |
| 1001 again | item 1 × 2 | Log: "already handled", and stock did not change |
| `this is not json` | – | Log: "Could not read", and the message is in `inventory.order-created.dead` with `x-first-death-reason: rejected` |
| 9300 (SQL Server stopped) | item 5 × 1 | Log: retry 1, 2 and 3 of 3 (shared by both replicas), then "dead-lettering it" |

The answers arrived as `stock.reserved` and `stock.rejected` with the right `correlation_id`.

## Trade-offs and future work

- **Dead letters need a human.** Nothing reads `inventory.order-created.dead` automatically. In production I would alert on it and add a way to send messages back to the main queue after the fault is fixed. An order whose message is dead-lettered stays `Pending` in the monolith until then.
- **Fixed retry delay.** Every retry waits 2 seconds. Exponential backoff would be gentler on a database that is restarting. An outage longer than the four attempts (about 25 seconds in my test, because each attempt also waits for the SQL connection timeout) sends the message to the dead-letter queue.
- **Duplicate answers.** Because of at-least-once delivery, the monolith can get the same `stock.reserved` twice. Its status update must therefore be idempotent too.
