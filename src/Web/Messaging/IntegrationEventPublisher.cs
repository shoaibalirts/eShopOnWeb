using Mediator;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Events;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Microsoft.eShopWeb.Web.Messaging;

// The public order.created message. Inventory reads it as OrderCreatedMessage.
public record OrderCreatedIntegrationEvent(int OrderId, IReadOnlyList<OrderCreatedLine> Lines);

public record OrderCreatedLine(int CatalogItemId, int Quantity);

// Listens to the in-process OrderCreatedEvent and publishes order.created to RabbitMQ.
public class IntegrationEventPublisher(
    RabbitMqPublisher publisher,
    IOptions<StockCheckOptions> stockCheck,
    ILogger<IntegrationEventPublisher> logger) : INotificationHandler<OrderCreatedEvent>
{
    public async ValueTask Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        if (!stockCheck.Value.Enabled)
        {
            return;
        }

        var message = Translate(domainEvent.Order);

        // CorrelationId = OrderId on every log line about this order, in all services.
        using var correlation = LogContext.PushProperty("CorrelationId", message.OrderId.ToString());
        try
        {
            await publisher.PublishAsync(RabbitMqTopology.OrderCreatedKey, message, message.OrderId.ToString(), cancellationToken);
            logger.LogInformation("Published order.created for order {OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            // The order is already saved, so I don't fail the checkout. The order stays Pending.
            // A transactional outbox would fix this, but it is out of scope (future work).
            logger.LogError(ex, "Could not publish order.created for order {OrderId}, it stays Pending", message.OrderId);
        }
    }

    // Message Translator: from the monolith's Order entity to the small public contract.
    // Only the order Id and what was bought; no prices, buyer or address. Units becomes Quantity.
    public static OrderCreatedIntegrationEvent Translate(Order order) =>
        new(order.Id, order.OrderItems
            .Select(item => new OrderCreatedLine(item.ItemOrdered.CatalogItemId, item.Units))
            .ToList());
}
