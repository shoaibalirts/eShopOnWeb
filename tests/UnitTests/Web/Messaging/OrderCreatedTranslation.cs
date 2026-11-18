using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Microsoft.eShopWeb.Web.Messaging;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Messaging;

public class OrderCreatedTranslation
{
    [Fact]
    public void MapsEveryOrderItemToALineWithCatalogItemIdAndQuantity()
    {
        var mug = new CatalogItemOrdered(2, "Mug", "mug.png");
        var shirt = new CatalogItemOrdered(3, "T-Shirt", "shirt.png");
        var order = new Order("buyer", new AddressBuilder().WithDefaultValues(),
            [new OrderItem(mug, 8.50m, 4), new OrderItem(shirt, 12m, 1)]);

        var message = IntegrationEventPublisher.Translate(order);

        Assert.Equal([new OrderCreatedLine(2, 4), new OrderCreatedLine(3, 1)], message.Lines);
    }

    // Contract test: this is exactly the JSON Inventory's OrderCreatedMessage reads.
    // If someone renames a property here, this test fails before Inventory breaks in production.
    [Fact]
    public void SerializesToTheContractInventoryExpects()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var json = JsonSerializer.Serialize(IntegrationEventPublisher.Translate(order), RabbitMqTopology.Json);

        Assert.Equal("""{"orderId":0,"lines":[{"catalogItemId":234,"quantity":3}]}""", json);
    }

    [Fact]
    public void LeavesOutPricesBuyerAndAddress()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var json = JsonSerializer.Serialize(IntegrationEventPublisher.Translate(order), RabbitMqTopology.Json);

        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buyer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
    }
}
