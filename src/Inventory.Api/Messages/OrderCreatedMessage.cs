namespace Inventory.Api.Messages;

// The order.created message: only what Inventory needs, nothing about prices, buyers or addresses.
public record OrderCreatedMessage(int OrderId, IReadOnlyList<OrderLine> Lines);

public record OrderLine(int CatalogItemId, int Quantity);
