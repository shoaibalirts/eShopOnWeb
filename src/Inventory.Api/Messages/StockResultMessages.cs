namespace Inventory.Api.Messages;

// Published on stock.reserved. The monolith marks the order Confirmed.
public record StockReservedMessage(int OrderId);

// Published on stock.rejected. The monolith marks the order Cancelled.
public record StockRejectedMessage(int OrderId, string Reason);
