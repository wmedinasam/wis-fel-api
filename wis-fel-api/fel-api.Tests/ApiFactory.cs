using fel_api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace fel_api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ClientIssuer = "https://idp.test";
    public const string ClientAudience = "test-client";
    public static readonly SymmetricSecurityKey ClientSigningKey = new(Enumerable.Repeat((byte)42, 64).ToArray());

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
            // Replaces the external IdP with a local test key so tenant endpoints can be exercised.
            services.PostConfigure<JwtBearerOptions>("ClientJwt", options =>
            {
                options.Authority = null;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = ClientIssuer,
                    ValidAudience = ClientAudience,
                    IssuerSigningKey = ClientSigningKey
                };
            });
        });
    }
}