using System.Net.Http.Json;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Microsoft.eShopWeb.Web.Services;

// Asks Inventory (through the gateway) how many of a product are in stock, for the catalog page.
public class InventoryAvailabilityService(HttpClient httpClient, ILogger<InventoryAvailabilityService> logger)
{
    public const string Unknown = "Availability unknown";

    public async Task<string> GetAvailabilityAsync(int catalogItemId, CancellationToken cancellationToken)
    {
        try
        {
            var stock = await httpClient.GetFromJsonAsync<StockLevel>($"api/inventory/{catalogItemId}", cancellationToken);
            if (stock is null)
            {
                return Unknown;
            }
            return stock.Quantity > 0 ? $"In stock: {stock.Quantity}" : "Out of stock";
        }
        catch (Exception ex) when (ex is HttpRequestException or BrokenCircuitException or TimeoutRejectedException)
        {
            // Fallback: the page still works when Inventory is down, it just doesn't know the stock.
            logger.LogWarning("No availability for item {CatalogItemId}: {Error}", catalogItemId, ex.GetType().Name);
            return Unknown;
        }
    }

    private record StockLevel(int CatalogItemId, int Quantity);
}

public static class InventoryAvailabilityServiceExtensions
{
    public static IHttpClientBuilder AddInventoryAvailability(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHttpClient<InventoryAvailabilityService>(client =>
            client.BaseAddress = new Uri(configuration["Inventory:BaseUrl"] ?? "http://localhost:5000/"));

        // Aspire's service defaults add a standard resilience handler to every HttpClient.
        // I replace it with my own pipeline, so I know exactly what happens.
        // RemoveAllResilienceHandlers is marked experimental, but it is the documented way to do this.
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.AddResilienceHandler("inventory", pipeline =>
        {
            // Outer: the circuit breaker counts failures, including timeouts from the inner strategy.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,                          // open when half of the calls fail...
                MinimumThroughput = 5,                       // ...out of at least 5 calls...
                SamplingDuration = TimeSpan.FromSeconds(30), // ...in the last 30 seconds,
                BreakDuration = TimeSpan.FromSeconds(30)     // then fail fast for 30 seconds.
            });
            // Inner: don't let a slow Inventory hold up the page.
            pipeline.AddTimeout(TimeSpan.FromSeconds(2));
        });
        return builder;
    }
}
