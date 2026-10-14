using fel_api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace fel_api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FelApi"] = "Server=127.0.0.1;Database=Tests;User Id=unused;Password=unused",
                ["Jwt:Issuer"] = "https://test-api.example",
                ["Jwt:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)17, 64).ToArray()),
                ["Jwt:PlatformAudience"] = "test-platform",
                ["Jwt:AgentAudience"] = "test-agent",
                ["ClientJwt:Authority"] = "",
                ["ClientJwt:Audience"] = "",
                ["ClientJwt:RequireHttpsMetadata"] = "true",
                ["RateLimiting:PlatformLogin:PermitLimit"] = "2",
                ["RateLimiting:PlatformLogin:WindowSeconds"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<FelApiDbContext>();
            services.RemoveAll<DbContextOptions<FelApiDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FelApiDbContext>>();
            services.AddDbContext<FelApiDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}