using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Messages;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Inventory.Tests.Services;

public class StockReservationServiceReserve
{
    // A fresh in-memory database per test, with item 1 = 10 units and item 2 = 3 units.
    private readonly InventoryDbContext _db;
    private readonly StockReservationService _service;

    public StockReservationServiceReserve()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new InventoryDbContext(options);
        _db.StockItems.AddRange(new StockItem(1, 10), new StockItem(2, 3));

        // SQL Server fills RowVersion itself. The in-memory provider doesn't, so the test sets a dummy value.
        // This also means these tests do not test concurrency.
        foreach (var entry in _db.ChangeTracker.Entries<StockItem>())
        {
            entry.Property(s => s.RowVersion).CurrentValue = new byte[8];
        }
        _db.SaveChanges();
        _service = new StockReservationService(_db, NullLogger<StockReservationService>.Instance);
    }

    private int QuantityOf(int catalogItemId) => _db.StockItems.Single(s => s.CatalogItemId == catalogItemId).Quantity;

    [Fact]
    public async Task ReservesEveryLineWhenThereIsEnoughStock()
    {
        var order = new OrderCreatedMessage(100, [new OrderLine(1, 4), new OrderLine(2, 3)]);

        var result = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Reserved, result.Status);
        Assert.Equal(6, QuantityOf(1));
        Assert.Equal(0, QuantityOf(2));
    }

    [Fact]
    public async Task RejectsWholeOrderAndChangesNoStockWhenOneLineIsShort()
    {
        // Line 1 fits, line 2 does not: all or none means line 1 is not reserved either.
        var order = new OrderCreatedMessage(101, [new OrderLine(1, 4), new OrderLine(2, 5)]);

        var result = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Rejected, result.Status);
        Assert.Contains("item 2", result.Reason);
        Assert.Equal(10, QuantityOf(1));
        Assert.Equal(3, QuantityOf(2));
    }

    [Fact]
    public async Task RejectsItemThatIsNotInInventory()
    {
        var order = new OrderCreatedMessage(102, [new OrderLine(99, 1)]);

        var result = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Rejected, result.Status);
        Assert.Contains("not in inventory", result.Reason);
    }

    [Fact]
    public async Task RejectsOrderWithNoLines()
    {
        var order = new OrderCreatedMessage(103, []);

        var result = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task ReservesOnlyOnceWhenTheSameOrderArrivesTwice()
    {
        var order = new OrderCreatedMessage(104, [new OrderLine(1, 4)]);

        var first = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);
        var second = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Reserved, first.Status);
        Assert.Equal(ReservationStatus.Reserved, second.Status);
        Assert.Equal(6, QuantityOf(1));
        Assert.Equal(1, _db.Reservations.Count());
    }

    [Fact]
    public async Task AddsUpLinesForTheSameItem()
    {
        // 2 + 2 = 4 is more than the 3 in stock, even though each line alone would fit.
        var order = new OrderCreatedMessage(105, [new OrderLine(2, 2), new OrderLine(2, 2)]);

        var result = await _service.ReserveAsync(order, TestContext.Current.CancellationToken);

        Assert.Equal(ReservationStatus.Rejected, result.Status);
        Assert.Equal(3, QuantityOf(2));
    }
}
