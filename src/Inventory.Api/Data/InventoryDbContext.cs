using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

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

        modelBuilder.Entity<Reservation>(b =>
        {
            b.HasKey(r => r.OrderId);
            b.Property(r => r.OrderId).ValueGeneratedNever();
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Reason).HasMaxLength(200);
        });
    }
}
