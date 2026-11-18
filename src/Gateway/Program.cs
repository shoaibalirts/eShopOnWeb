var builder = WebApplication.CreateBuilder(args);

// Routes and destinations come from the "ReverseProxy" section in appsettings.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// /health is more specific than the catch-all route, so the gateway answers it itself.
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program { }
