> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit. The monolith side is added on Thursday 19/11.

# Message channels and routing

## Why messages

When a customer places an order, the monolith must not wait for Inventory. If checkout called Inventory over HTTP, the shop could not take orders while Inventory was down. So the monolith saves the order as `Pending` and publishes a message, and Inventory answers with another message. The two services don't need to be running at the same time.

I use RabbitMQ with the library RabbitMQ.Client 7. I chose it over MassTransit so I could see and explain every part: exchanges, queues, bindings and acks.

## The channels

| Name | Type | Role |
|---|---|---|
| `eshop.events` | Topic exchange, durable | Receives every event and routes it by routing key |
| `inventory.order-created` | Queue, durable, bound to `order.created` | Inventory's inbox |
| Monolith queue, bound to `stock.*` | Queue, durable | The monolith's inbox for the answers (Thursday) |

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

The consumer is `OrderCreatedConsumer` (`src/Inventory.Api/Messaging/`), a `BackgroundService`. At startup it declares the exchange, the queue and the binding. Declaring is safe to repeat: RabbitMQ only creates what is missing. Start order still matters. An exchange doesn't store messages, so if the monolith published `order.created` before Inventory had created its queue, the message would be dropped. **[Thursday: describe how I handle this in the monolith.]**

It uses manual ack (`autoAck: false`). For each message it does three things in this order:

1. Reserve stock with `StockReservationService.ReserveAsync`.
2. Publish `stock.reserved` or `stock.rejected`. The channel uses publisher confirms, so this step waits until RabbitMQ has stored the answer.
3. Ack the `order.created` message.

If Inventory crashes before step 3, RabbitMQ delivers the message again. This means a message can be handled more than once, but it is never lost (at-least-once delivery). That is safe because `ReserveAsync` is idempotent: the second time it returns the first answer without changing stock.

There are two kinds of errors:

- **A poison message**, such as invalid JSON, can never succeed. The consumer logs an error and nacks with `requeue: false`, and RabbitMQ drops it.
- **A temporary error**, such as the database being down, may succeed later. The consumer waits 2 seconds and nacks with `requeue: true`, so the message goes back into the queue.

## How I tested it

I published four messages to `eshop.events` with the RabbitMQ management API. To see the answers, I bound a test queue to `stock.*`.

| Order | Lines | Result |
|---|---|---|
| 1001 | item 1 × 2 | `Reserved`, item 1 went from 25 to 23 |
| 1002 | item 1 × 1, item 2 × 5 | `Rejected`, and item 1 stayed at 23 (all or none) |
| 1001 again | item 1 × 2 | Log: "already handled", and stock did not change |
| `this is not json` | – | Log: "Could not read", and the message was dropped |

The answers arrived as `stock.reserved` and `stock.rejected` with the right `correlation_id`.

## Trade-offs and future work

- **No dead-letter exchange.** I cut it on 17/11. Poison messages are dropped, and the log is the only trace. A dead-letter exchange would keep them so I could inspect them.
- **No retry limit.** A message that always fails with a temporary error is retried every 2 seconds forever. A retry counter and a dead-letter exchange would fix this.
- **Duplicate answers.** Because of at-least-once delivery, the monolith can get the same `stock.reserved` twice. Its status update must therefore be idempotent too.
