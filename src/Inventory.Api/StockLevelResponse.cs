namespace Inventory.Api;

public record StockLevelResponse(int CatalogItemId, int Quantity);

public record SetStockRequest(int Quantity);
