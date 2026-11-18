using Microsoft.eShopWeb.Web.Messaging;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Messaging;

// Inventory declares the same queue (OrderCreatedConsumer.QueueArguments) and has the same test.
// If the two sides drift apart, RabbitMQ refuses the declare with 406 PRECONDITION_FAILED at runtime.
public class QueueTopologyContract
{
    [Fact]
    public void InventoryQueueDeadLettersToEshopDlx()
    {
        Assert.Equal("inventory.order-created", RabbitMqTopology.InventoryQueue);
        Assert.Equal("eshop.dlx", RabbitMqTopology.InventoryQueueArguments["x-dead-letter-exchange"]);
        Assert.Single(RabbitMqTopology.InventoryQueueArguments);
    }
}
