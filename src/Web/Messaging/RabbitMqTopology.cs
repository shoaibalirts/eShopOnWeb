using System.Text.Json;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.Web.Messaging;

// The names must match Inventory's OrderCreatedConsumer. I copy them instead of sharing a library,
// so the two services have no code dependency on each other.
public static class RabbitMqTopology
{
    public const string Exchange = "eshop.events";
    public const string OrderCreatedKey = "order.created";
    public const string StockReservedKey = "stock.reserved";
    public const string StockRejectedKey = "stock.rejected";
    public const string InventoryQueue = "inventory.order-created";
    public const string MonolithQueue = "monolith.stock-results";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Both services declare the whole topology. An exchange doesn't store messages, so if the monolith
    // published before Inventory's queue existed, order.created would be lost. Declaring is idempotent.
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(InventoryQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(InventoryQueue, Exchange, OrderCreatedKey, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(MonolithQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(MonolithQueue, Exchange, "stock.*", cancellationToken: cancellationToken);
    }

    public static ConnectionFactory CreateFactory(RabbitMqOptions options, string clientName) => new()
    {
        HostName = options.HostName,
        UserName = options.UserName,
        Password = options.Password,
        ClientProvidedName = clientName,
        // Fail fast at checkout if RabbitMQ is down, instead of making the customer wait.
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
    };
}
