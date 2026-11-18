using System.Net;
using System.Net.Http.Json;
using Inventory.Api;
using Xunit;

namespace Inventory.Tests.Api;

// HTTP-level tests of the two Inventory endpoints. The gateway's JWT check is tested in Gateway.Tests;
// here I test what Inventory itself answers.
public class StockApi : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client;

    public StockApi(InventoryApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsTheSeededStockLevel()
    {
        var stock = await _client.GetFromJsonAsync<StockLevelResponse>("/api/inventory/3", Token);

        Assert.Equal(new StockLevelResponse(3, 25), stock);
    }

    [Fact]
    public async Task ReturnsNotFoundForAnUnknownItem()
    {
        var response = await _client.GetAsync("/api/inventory/999", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetsTheStockLevel()
    {
        var response = await _client.PutAsJsonAsync("/api/inventory/4/stock", new SetStockRequest(40), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stock = await _client.GetFromJsonAsync<StockLevelResponse>("/api/inventory/4", Token);
        Assert.Equal(40, stock!.Quantity);
    }

    [Fact]
    public async Task RejectsANegativeStockLevel()
    {
        var response = await _client.PutAsJsonAsync("/api/inventory/5/stock", new SetStockRequest(-1), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stock = await _client.GetFromJsonAsync<StockLevelResponse>("/api/inventory/5", Token);
        Assert.Equal(25, stock!.Quantity);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenRestockingAnUnknownItem()
    {
        var response = await _client.PutAsJsonAsync("/api/inventory/999/stock", new SetStockRequest(5), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublishesAnOpenApiDocumentWithARelativeServer()
    {
        var document = await _client.GetStringAsync("/api/inventory/openapi/v1.json", Token);

        Assert.Contains("/api/inventory/{itemId}/stock", document);
        Assert.Contains("\"url\": \"/\"", document);
    }

    [Fact]
    public async Task AnswersHealthy()
    {
        var response = await _client.GetAsync("/health", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
