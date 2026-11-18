using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.Web.Messaging;

// Singleton: one connection for the whole app, opened on the first publish.
public sealed class RabbitMqPublisher(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    // A channel must not be used by two threads at once, and two customers can check out at the same time.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync<T>(string routingKey, T message, string correlationId, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, RabbitMqTopology.Json);
        var properties = new BasicProperties
        {
            CorrelationId = correlationId,
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            Type = routingKey,
            DeliveryMode = DeliveryModes.Persistent
        };
        TraceContext.Inject(properties);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            // With publisher confirms this waits until RabbitMQ has stored the message.
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, mandatory: false, properties, body, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_connection is not { IsOpen: true })
        {
            _connection = await RabbitMqTopology.CreateFactory(options.Value, "eshop-web-publisher")
                .CreateConnectionAsync(cancellationToken);
        }

        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(_channel, cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
