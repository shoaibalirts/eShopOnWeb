using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// The monolith signs its tokens with HMAC-SHA256 (IdentityTokenClaimService). The gateway checks the
// signature and expiry with the same key, so Inventory never has to deal with tokens itself.
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the claim names as they are in the token ("role"), instead of renaming them to long URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(signingKey)),
            // The monolith's tokens have no issuer or audience, so I can't check them.
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            // The monolith writes the claims as "unique_name" and "role" inside the token.
            NameClaimType = "unique_name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Used by the restock route in appsettings.json.
    options.AddPolicy("admin", policy => policy.RequireAuthenticatedUser().RequireRole("Administrators"));
});

// Routes and destinations come from the "ReverseProxy" section in appsettings.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// /health is more specific than the catch-all route, so the gateway answers it itself.
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }
