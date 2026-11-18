using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Gateway.Tests;

public class RestockAuthorization(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private async Task<HttpStatusCode> PutStockAsync(string? token)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/inventory/2/stock")
        {
            Content = JsonContent.Create(new { quantity = 10 })
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task RejectsRequestWithoutToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await PutStockAsync(null));
    }

    [Fact]
    public async Task RejectsTokenSignedWithAnotherKey()
    {
        var forged = GatewayFactory.CreateToken("Administrators", key: "SomeoneElsesKeyThatIsAlsoLongEnough1234");

        Assert.Equal(HttpStatusCode.Unauthorized, await PutStockAsync(forged));
    }

    [Fact]
    public async Task RejectsExpiredToken()
    {
        var expired = GatewayFactory.CreateToken("Administrators", expired: true);

        Assert.Equal(HttpStatusCode.Unauthorized, await PutStockAsync(expired));
    }

    [Fact]
    public async Task ForbidsUserWithoutAdministratorsRole()
    {
        var customer = GatewayFactory.CreateToken("Customer");

        Assert.Equal(HttpStatusCode.Forbidden, await PutStockAsync(customer));
    }

    [Fact]
    public async Task ForwardsAdministratorToInventory()
    {
        var admin = GatewayFactory.CreateToken("Administrators");

        // 502: the gateway let it through and tried to reach Inventory, which isn't running in this test.
        Assert.Equal(HttpStatusCode.BadGateway, await PutStockAsync(admin));
    }

    [Fact]
    public async Task ReadingStockNeedsNoToken()
    {
        var response = await factory.CreateClient().GetAsync("/api/inventory/2", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
