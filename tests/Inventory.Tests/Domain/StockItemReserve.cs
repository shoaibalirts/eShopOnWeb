using Inventory.Api.Domain;
using Xunit;

namespace Inventory.Tests.Domain;

public class StockItemReserve
{
    [Fact]
    public void ReducesQuantity()
    {
        var item = new StockItem(catalogItemId: 1, quantity: 10);

        item.Reserve(3);

        Assert.Equal(7, item.Quantity);
    }

    [Fact]
    public void CanTakeTheLastUnit()
    {
        var item = new StockItem(catalogItemId: 1, quantity: 3);

        item.Reserve(3);

        Assert.Equal(0, item.Quantity);
    }

    [Fact]
    public void ThrowsAndKeepsQuantityWhenNotEnoughStock()
    {
        var item = new StockItem(catalogItemId: 1, quantity: 3);

        Assert.Throws<InvalidOperationException>(() => item.Reserve(4));
        Assert.Equal(3, item.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesZeroOrNegativeQuantity(int quantity)
    {
        var item = new StockItem(catalogItemId: 1, quantity: 3);

        Assert.False(item.CanReserve(quantity));
        Assert.Throws<InvalidOperationException>(() => item.Reserve(quantity));
    }
}
