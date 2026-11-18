using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Tests;

// Runs the real gateway in memory. Inventory is not running: its address points to a closed port,
// so a request that passes the gateway's checks ends as 502 Bad Gateway. That is how the tests
// tell "stopped by the gateway" (401, 403, 429) from "let through" (502).
public class GatewayFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "TestSigningKeyThatIsLongEnoughForHmacSha256";

    public int PermitLimit { get; init; } = 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", "60");
        builder.UseSetting("ReverseProxy:Clusters:inventory:Destinations:inventory1:Address", "http://127.0.0.1:1/");
    }

    // Builds a token the same way the monolith's IdentityTokenClaimService does.
    public static string CreateToken(string role, string key = SigningKey, bool expired = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test@microsoft.com"), new Claim(ClaimTypes.Role, role)]),
            NotBefore = expired ? now.AddHours(-2) : now,
            Expires = expired ? now.AddHours(-1) : now.AddDays(7),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)), SecurityAlgorithms.HmacSha256Signature)
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
