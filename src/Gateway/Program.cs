using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logs to the console and to Seq, like the monolith and Inventory.
builder.Services.AddSerilog(logger => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "gateway")
    .Enrich.WithProperty("Instance", Environment.MachineName)
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));

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

// Sliding window per client IP. A client that sends too many requests gets 429 before Inventory is called.
var permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100);
var window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 10));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = ((int)window.TotalSeconds).ToString();
        return ValueTask.CompletedTask;
    };

    // Used by the inventory routes in appsettings.json. A sliding window counts the requests of the last
    // 10 seconds in 5 segments of 2 s. I first used a fixed window, but it resets on a timer, so a burst
    // that crossed a reset got up to twice the limit through (I saw 110 × 200 in a live test).
    options.AddPolicy("inventory", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            SegmentsPerWindow = 5,
            QueueLimit = 0
        }));
});

// Routes and destinations come from the "ReverseProxy" section in appsettings.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// One log line per request through the gateway, with the status code and the time it took.
app.UseSerilogRequestLogging();

// Rate limiting first, so a flood of requests is stopped before any token is checked.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// /health is more specific than the catch-all route, so the gateway answers it itself.
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }
