using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace Microsoft.eShopWeb.Web.Messaging;

// Inventory's answer. Only Reason is read from the body; the order Id comes from CorrelationId.
public record StockResultMessage(int OrderId, string? Reason);

// Reads stock.reserved and stock.rejected and sets the order to Confirmed or Cancelled.
public class StockResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<StockResultConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The monolith is the shop, so it must not crash when RabbitMQ is down. I retry until it is up.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = await RabbitMqTopology.CreateFactory(options.Value, "eshop-web-consumer")
                    .CreateConnectionAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("RabbitMQ is not reachable ({Error}), retrying in 5 seconds", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _channel = await _connection!.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(delivery, stoppingToken);
        await _channel.BasicConsumeAsync(RabbitMqTopology.MonolithQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Listening on queue {Queue} for stock.*", RabbitMqTopology.MonolithQueue);
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var channel = _channel!;

        // Same TraceId as the checkout request, and CorrelationId = OrderId on every log line.
        using var activity = TraceContext.StartConsumer(delivery);
        using var correlation = LogContext.PushProperty("CorrelationId", delivery.BasicProperties.CorrelationId);

        // Correlation Identifier: the answer says which order it belongs to.
        if (!int.TryParse(delivery.BasicProperties.CorrelationId, out var orderId))
        {
            logger.LogError("{RoutingKey} message without a valid CorrelationId, dropping it", delivery.RoutingKey);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
            var order = await orders.GetByIdAsync(orderId, stoppingToken);

            if (order is null)
            {
                // Nothing to update, and retrying won't help.
                logger.LogWarning("Order {OrderId} not found for {RoutingKey}", orderId, delivery.RoutingKey);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            // The routing key says what happened. Confirm and Cancel only change a Pending order,
            // so a duplicate answer is harmless.
            if (delivery.RoutingKey == RabbitMqTopology.StockReservedKey)
            {
                order.Confirm();
            }
            else if (delivery.RoutingKey == RabbitMqTopology.StockRejectedKey)
            {
                order.Cancel();
            }

            await orders.UpdateAsync(order, stoppingToken);
            logger.LogInformation("Order {OrderId} is now {Status} ({RoutingKey}) {Reason}",
                orderId, order.Status, delivery.RoutingKey, ReadReason(delivery));

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Updating order {OrderId} failed, requeueing", orderId);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static string? ReadReason(BasicDeliverEventArgs delivery)
    {
        try
        {
            return JsonSerializer.Deserialize<StockResultMessage>(delivery.Body.Span, RabbitMqTopology.Json)?.Reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        if (_connection is not null) await _connection.CloseAsync(cancellationToken);
    }
}
