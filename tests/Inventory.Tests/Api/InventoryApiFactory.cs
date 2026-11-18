using Inventory.Api.Data;
using Inventory.Api.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Tests.Api;

// Runs the real Inventory API in memory: the real endpoints, validation and seed data.
// Only two things are swapped: SQL Server becomes an in-memory database, and the RabbitMQ
// consumer is removed, so the tests need neither Docker nor a broker.
public class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"inventory-api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
            services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            var consumer = services.Single(d => d.ImplementationType == typeof(OrderCreatedConsumer));
            services.Remove(consumer);
        });
    }
}
