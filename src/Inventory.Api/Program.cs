using Inventory.Api;
using Inventory.Api.Data;
using Inventory.Api.Messaging;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddScoped<StockReservationService>();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHostedService<OrderCreatedConsumer>();

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

// Admin restock. The gateway checks the JWT and the Administrators role before a request gets here,
// and Inventory has no public port, so it can only be reached through the gateway.
app.MapPut("/api/inventory/{itemId:int}/stock", async (int itemId, SetStockRequest request, InventoryDbContext db) =>
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity can't be negative.");
        }

        var item = await db.StockItems.FindAsync(itemId);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.SetQuantity(request.Quantity);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // An order reserved this item at the same moment (RowVersion changed). The admin should count again.
            return Results.Conflict("The stock changed while you were updating it. Try again.");
        }

        return Results.Ok(new StockLevelResponse(item.CatalogItemId, item.Quantity));
    })
    .WithName("SetStockLevel")
    .Produces<StockLevelResponse>()
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.Run();

public partial class Program { }
