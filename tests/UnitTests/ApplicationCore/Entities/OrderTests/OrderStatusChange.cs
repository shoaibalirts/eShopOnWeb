using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusChange
{
    [Fact]
    public void StartsAsPending()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void ConfirmMakesPendingOrderConfirmed()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void CancelMakesPendingOrderCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DuplicateConfirmChangesNothing()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.Confirm();
        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void LateConfirmDoesNotReopenCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.Cancel();
        order.Confirm();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
