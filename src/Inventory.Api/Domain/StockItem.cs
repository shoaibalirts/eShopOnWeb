namespace Inventory.Api.Domain;

public class StockItem
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private StockItem() {}

    public StockItem(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    // The Id of the product in the monolith's Catalog. It is a reference, not a foreign key,
    // because the catalog lives in another database.
    public int CatalogItemId { get; private set; }
    public int Quantity { get; private set; }

    // Concurrency token: SQL Server changes it on every update, so two buyers can't both take the last unit.
    public byte[] RowVersion { get; private set; }
}
