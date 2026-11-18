> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Message transformation

## The problem

Inside the monolith, an order is the entity `Order` in `ApplicationCore`. It has a buyer, a shipping address, prices, product names and picture links. When an order is created, `OrderService.CreateOrderAsync` raises the in-process domain event `OrderCreatedEvent`, which carries the whole `Order`.

Inventory must not depend on that model. If I sent the whole `Order` as JSON, Inventory would break every time the monolith changed its entity, and it would receive personal data (buyer and address) it has no reason to see. So the two services need a small, stable contract, and something that translates between the inside and the outside. That is the Message Translator pattern.

## The translator

The translator is `IntegrationEventPublisher` in `src/Web/Messaging/IntegrationEventPublisher.cs`. It is a Mediator notification handler for `OrderCreatedEvent`, so it runs next to the existing `OrderCreatedHandler` (which "sends" the order email by writing it to the log). I didn't have to change `OrderService` at all.

The translation itself is one static method:

```csharp
public static OrderCreatedIntegrationEvent Translate(Order order) =>
    new(order.Id, order.OrderItems
        .Select(item => new OrderCreatedLine(item.ItemOrdered.CatalogItemId, item.Units))
        .ToList());
```

| Inside the monolith (`Order`) | In `order.created` |
|---|---|
| `Id` | `orderId` |
| `OrderItems[].ItemOrdered.CatalogItemId` | `lines[].catalogItemId` |
| `OrderItems[].Units` | `lines[].quantity` |
| `BuyerId`, `ShipToAddress`, `UnitPrice`, `ProductName`, `PictureUri`, `OrderDate` | *left out* |

The result is serialized as camelCase JSON with `JsonSerializerDefaults.Web`:

```json
{"orderId":1,"lines":[{"catalogItemId":1,"quantity":2}]}
```

Inventory reads exactly this shape as its own record, `OrderCreatedMessage`. The two services have separate classes for the same message on purpose. They share no code library, so neither has to be rebuilt when the other changes, as long as the JSON stays the same.

The messages also carry metadata in the RabbitMQ properties, not in the body: `CorrelationId` (the order Id), a new `MessageId`, `ContentType = application/json`, `Type` (the routing key) and persistent delivery.

## Testing the contract

Because the two services have separate classes, a renamed property would only fail at runtime. I protect the contract with three unit tests in `tests/UnitTests/Web/Messaging/OrderCreatedTranslation.cs`:

| Test | What it proves |
|---|---|
| `MapsEveryOrderItemToALineWithCatalogItemIdAndQuantity` | Two order items become two lines with the right Id and quantity |
| `SerializesToTheContractInventoryExpects` | The JSON is exactly `{"orderId":0,"lines":[{"catalogItemId":234,"quantity":3}]}` |
| `LeavesOutPricesBuyerAndAddress` | No price, buyer or address leaks into the message |

The second test is a simple consumer-driven contract test: it fixes the JSON that Inventory depends on. If someone renames `Quantity` in the monolith, this test fails before Inventory breaks.

## The other direction

The answers, `stock.reserved` and `stock.rejected`, are translated the other way in `StockResultConsumer`. The routing key decides the action (`Confirm()` or `Cancel()`), the `CorrelationId` property gives the order Id, and the body only adds the `reason` for the log. The monolith never sees Inventory's `Reservation` entity.

## Trade-offs

- **Duplicate classes.** The contract exists twice (`OrderCreatedIntegrationEvent` in Web, `OrderCreatedMessage` in Inventory). That is more code, but it keeps the services independent. A shared NuGet package would remove the duplication but couple their releases.
- **No versioning.** The message has no version number. If the contract has to change, I would add new fields as optional, or publish a new routing key such as `order.created.v2` for a while.
- **The dual-write problem.** The order is saved first and the message is published afterwards. If RabbitMQ is down at that moment, the publish fails. I log the error and let the checkout succeed, so the order stays `Pending`. The proper fix is a Transactional Outbox: save the message in the same database transaction as the order, and publish it later. This is future work.
