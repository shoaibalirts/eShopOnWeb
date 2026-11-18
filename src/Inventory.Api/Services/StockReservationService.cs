using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Messages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public class StockReservationService(InventoryDbContext db, ILogger<StockReservationService> logger)
{
    public async Task<Reservation> ReserveAsync(OrderCreatedMessage order, CancellationToken cancellationToken = default)
    {
        // Idempotent receiver: an order I already handled gets the same answer again, and no stock changes.
        var existing = await db.Reservations.FindAsync([order.OrderId], cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Order {OrderId} was already handled ({Status}), so nothing changes",
                order.OrderId, existing.Status);
            return existing;
        }

        // The same product can be on two lines, so I add them up before checking.
        var wanted = order.Lines
            .GroupBy(line => line.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var ids = wanted.Keys.ToList();
        var stock = await db.StockItems
            .Where(item => ids.Contains(item.CatalogItemId))
            .ToDictionaryAsync(item => item.CatalogItemId, cancellationToken);

        // All or none: check every line before changing any stock.
        var reason = FindRejectReason(wanted, stock);
        Reservation reservation;
        if (reason is null)
        {
            foreach (var (catalogItemId, quantity) in wanted)
            {
                stock[catalogItemId].Reserve(quantity);
            }
            reservation = Reservation.Reserved(order.OrderId);
        }
        else
        {
            reservation = Reservation.Rejected(order.OrderId, reason);
        }

        db.Reservations.Add(reservation);

        // One SaveChanges is one transaction: the stock changes and the reservation are saved together, or not at all.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId}: {Status} {Reason}", order.OrderId, reservation.Status, reason);
        return reservation;
    }

    private static string? FindRejectReason(Dictionary<int, int> wanted, Dictionary<int, StockItem> stock)
    {
        if (wanted.Count == 0)
        {
            return "The order has no lines.";
        }

        foreach (var (catalogItemId, quantity) in wanted)
        {
            if (!stock.TryGetValue(catalogItemId, out var item))
            {
                return $"Item {catalogItemId} is not in inventory.";
            }
            if (!item.CanReserve(quantity))
            {
                return $"Not enough stock for item {catalogItemId}: wanted {quantity}, have {item.Quantity}.";
            }
        }

        return null;
    }
}
