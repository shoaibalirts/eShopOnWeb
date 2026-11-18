using System.Text.Json;
using Inventory.Api.Domain;
using Inventory.Api.Messages;
using Inventory.Api.Services;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inventory.Api.Messaging;

// Reads order.created from the queue, reserves stock, and answers with stock.reserved or stock.rejected.
public class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    public const string Exchange = "eshop.events";
    public const string Queue = "inventory.order-created";
    public const string OrderCreatedKey = "order.created";
    public const string StockReservedKey = "stock.reserved";
    public const string StockRejectedKey = "stock.rejected";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Value.HostName,
            UserName = options.Value.UserName,
            Password = options.Value.Password,
            ClientProvidedName = "inventory-api"
        };
        _connection = await factory.CreateConnectionAsync(stoppingToken);

        // Publisher confirms: BasicPublishAsync waits until RabbitMQ has the result message,
        // so I never ack order.created before stock.reserved/rejected is safe.
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        // Declaring is safe to repeat: RabbitMQ only creates what is missing.
        await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(Queue, Exchange, OrderCreatedKey, cancellationToken: stoppingToken);

        // Take at most 10 unacked messages at a time, so two replicas share the work.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(delivery, stoppingToken);

        // autoAck: false = manual ack. RabbitMQ keeps the message until I say it is done.
        await _channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Listening on queue {Queue} for {RoutingKey}", Queue, OrderCreatedKey);
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var channel = _channel!;

        OrderCreatedMessage? order;
        try
        {
            order = JsonSerializer.Deserialize<OrderCreatedMessage>(delivery.Body.Span, Json);
        }
        catch (JsonException)
        {
            order = null;
        }

        if (order is null || order.Lines is null)
        {
            // Poison message: it will never parse, so retrying is pointless. Without a dead-letter
            // exchange (cut on 17/11) RabbitMQ drops it, and the log is the only trace.
            logger.LogError("Could not read order.created message {MessageId}, dropping it", delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        try
        {
            Reservation reservation;
            using (var scope = scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<StockReservationService>();
                reservation = await service.ReserveAsync(order, stoppingToken);
            }

            await PublishResultAsync(channel, reservation, stoppingToken);

            // Ack last: if I crash before this line, RabbitMQ delivers the message again,
            // and the idempotent ReserveAsync returns the same answer without touching stock.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A temporary problem (database down, concurrency conflict): put the message back and try again.
            logger.LogWarning(ex, "Order {OrderId} failed, requeueing", order.OrderId);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static async Task PublishResultAsync(IChannel channel, Reservation reservation, CancellationToken cancellationToken)
    {
        var (routingKey, body) = reservation.Status == ReservationStatus.Reserved
            ? (StockReservedKey, JsonSerializer.SerializeToUtf8Bytes(new StockReservedMessage(reservation.OrderId), Json))
            : (StockRejectedKey, JsonSerializer.SerializeToUtf8Bytes(new StockRejectedMessage(reservation.OrderId, reservation.Reason!), Json));

        var properties = new BasicProperties
        {
            CorrelationId = reservation.OrderId.ToString(),
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            Type = routingKey,
            DeliveryMode = DeliveryModes.Persistent
        };

        await channel.BasicPublishAsync(Exchange, routingKey, mandatory: false, properties, body, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        if (_connection is not null) await _connection.CloseAsync(cancellationToken);
    }
}
