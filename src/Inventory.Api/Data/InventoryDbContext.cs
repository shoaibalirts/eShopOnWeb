using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(b =>
        {
            b.HasKey(s => s.CatalogItemId);
            b.Property(s => s.CatalogItemId).ValueGeneratedNever();
            b.Property(s => s.RowVersion).IsRowVersion();

            // Starting stock for the 12 products the monolith seeds in CatalogContextSeed.
            // Item 2 (the mug) has only 3, so I can demo a rejected order.
            b.HasData(Enumerable.Range(1, 12)
                .Select(id => new { CatalogItemId = id, Quantity = id == 2 ? 3 : 25 }));
        });
    }
}
