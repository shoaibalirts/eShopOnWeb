using System.Net;
using Xunit;

namespace Gateway.Tests;

public class InventoryRateLimit
{
    [Fact]
    public async Task ReturnsTooManyRequestsWhenTheLimitIsUsedUp()
    {
        using var factory = new GatewayFactory { PermitLimit = 3 };
        var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.GetAsync("/api/inventory/1", cancellationToken);
            statuses.Add(response.StatusCode);
        }

        // The first 3 pass the gateway (502, because Inventory isn't running), the 4th is stopped.
        Assert.All(statuses.Take(3), status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }

    [Fact]
    public async Task TellsTheClientWhenToTryAgain()
    {
        using var factory = new GatewayFactory { PermitLimit = 1 };
        var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.GetAsync("/api/inventory/1", cancellationToken);
        var rejected = await client.GetAsync("/api/inventory/1", cancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("60", rejected.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task DoesNotLimitTheShopPages()
    {
        using var factory = new GatewayFactory { PermitLimit = 1 };
        var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        // The catch-all route to the monolith has no rate limiter policy.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/", cancellationToken);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
