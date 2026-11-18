using System.Net;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

public class InventoryAvailability
{
    // A fake network: counts calls and answers with whatever the test chooses.
    private class FakeInventory(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    // Uses the real registration from Program.cs (with the Polly pipeline), only the network is fake.
    private static InventoryAvailabilityService CreateService(FakeInventory fake)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryAvailability(new ConfigurationBuilder().Build())
            .ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<InventoryAvailabilityService>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ShowsTheQuantityInStock()
    {
        var service = CreateService(new FakeInventory(() => Json("""{"catalogItemId":1,"quantity":5}""")));

        Assert.Equal("In stock: 5", await service.GetAvailabilityAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShowsOutOfStockAtZero()
    {
        var service = CreateService(new FakeInventory(() => Json("""{"catalogItemId":1,"quantity":0}""")));

        Assert.Equal("Out of stock", await service.GetAvailabilityAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FallsBackWhenInventoryFails()
    {
        var service = CreateService(new FakeInventory(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Assert.Equal(InventoryAvailabilityService.Unknown, await service.GetAvailabilityAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpensTheCircuitAndStopsCallingInventory()
    {
        var fake = new FakeInventory(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(fake);
        var cancellationToken = TestContext.Current.CancellationToken;

        // 5 failures (the minimum throughput) open the circuit...
        for (var i = 0; i < 5; i++)
        {
            await service.GetAvailabilityAsync(1, cancellationToken);
        }
        // ...so the next 10 calls fail fast without reaching the network.
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(InventoryAvailabilityService.Unknown, await service.GetAvailabilityAsync(1, cancellationToken));
        }

        Assert.Equal(5, fake.Calls);
    }
}
