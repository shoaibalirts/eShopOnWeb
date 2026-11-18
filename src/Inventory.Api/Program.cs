using Inventory.Api;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddScoped<StockReservationService>();

builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Create or update InventoryDb on startup, so a clean `docker compose up` works without manual steps.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Inventory API"));

app.MapHealthChecks("/health");

app.MapGet("/api/inventory/{itemId:int}", async (int itemId, InventoryDbContext db) =>
        await db.StockItems.FindAsync(itemId) is { } item
            ? Results.Ok(new StockLevelResponse(item.CatalogItemId, item.Quantity))
            : Results.NotFound())
    .WithName("GetStockLevel")
    .Produces<StockLevelResponse>()
    .Produces(StatusCodes.Status404NotFound);

app.Run();

public partial class Program { }
