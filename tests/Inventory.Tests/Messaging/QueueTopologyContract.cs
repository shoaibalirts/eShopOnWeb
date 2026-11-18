using Inventory.Api.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace Inventory.Tests.Messaging;

// The monolith declares the same queue (RabbitMqTopology.InventoryQueueArguments) and has the same test.
// If the two sides drift apart, RabbitMQ refuses the declare with 406 PRECONDITION_FAILED at runtime.
public class QueueTopologyContract
{
    [Fact]
    public void InventoryQueueDeadLettersToEshopDlx()
    {
        Assert.Equal("inventory.order-created", OrderCreatedConsumer.Queue);
        Assert.Equal("eshop.dlx", OrderCreatedConsumer.QueueArguments["x-dead-letter-exchange"]);
        Assert.Single(OrderCreatedConsumer.QueueArguments);
    }

    [Fact]
    public void RetryCountIsZeroForAFreshMessage()
    {
        Assert.Equal(0, OrderCreatedConsumer.RetryCount(new BasicProperties()));
    }

    [Fact]
    public void RetryCountIsReadFromTheHeader()
    {
        var properties = new BasicProperties { Headers = new Dictionary<string, object?> { ["x-retry-count"] = 2 } };

        Assert.Equal(2, OrderCreatedConsumer.RetryCount(properties));
    }
}
