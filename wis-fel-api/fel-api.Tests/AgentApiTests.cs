using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;
using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Agents;
using fel_api.Domain.Catalog;
using fel_api.Domain.Identity;
using fel_api.Domain.Subscriptions;
using fel_api.Domain.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace fel_api.Tests;

public sealed class AgentApiTests
{
    [Fact]
    public async Task PlatformLogin_AuthenticatesOnlyPlatformAdministrators()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await SeedUserAsync(factory, isPlatformAdministrator: true);

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = user.Email,
            Password = "Correct-Horse-2026!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task PlatformLogin_RejectsTenantUser()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await SeedUserAsync(factory, isPlatformAdministrator: false);

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = user.Email,
            Password = "Correct-Horse-2026!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClientMe_RequiresClientJwt()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformLogin_EnforcesConfiguredRateLimit()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await SeedUserAsync(factory, isPlatformAdministrator: true);
        Assert.Equal(2, factory.Services.GetRequiredService<IOptions<fel_api.RateLimiting.RateLimitOptions>>()
            .Value.PlatformLogin.PermitLimit);

        var first = await client.PostAsJsonAsync("/auth/login", new { Email = user.Email, Password = "Wrong-Password-2026!" });
        var second = await client.PostAsJsonAsync("/auth/login", new { Email = user.Email, Password = "Wrong-Password-2026!" });
        var limited = await client.PostAsJsonAsync("/auth/login", new { Email = user.Email, Password = "Wrong-Password-2026!" });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task HandshakeIssuesJwtAndHeartbeatReturnsLeasedCommand()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        var handshakeResponse = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            Secret = seeded.Secret,
            WorkerVersion = "1.2.3"
        });

        Assert.Equal(HttpStatusCode.OK, handshakeResponse.StatusCode);
        var tokens = await handshakeResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
            dbContext.PendingAgentCommands.Add(new PendingAgentCommand
            {
                AgentId = seeded.Agent.Id,
                CommandType = "refresh-configuration",
                Payload = "{}"
            });
            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var heartbeatResponse = await client.PostAsJsonAsync("/agent/heartbeat", new
        {
            WorkerVersion = "1.2.3",
            AcknowledgedCommandIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.OK, heartbeatResponse.StatusCode);
        var heartbeat = await heartbeatResponse.Content.ReadFromJsonAsync<HeartbeatResponse>();
        Assert.NotNull(heartbeat);
        var command = Assert.Single(heartbeat.PendingCommands);
        Assert.Equal("refresh-configuration", command.Type);
        Assert.True(command.LeaseExpiresAtUtc > heartbeat.ServerTimeUtc);
    }

    [Fact]
    public async Task AgentRefresh_RotatesTokenAndRejectsReuse()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        var handshakeResponse = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            Secret = seeded.Secret,
            WorkerVersion = "1.2.3"
        });
        var initialTokens = await handshakeResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(initialTokens);

        var firstRefresh = await client.PostAsJsonAsync("/agent/token/refresh", new
        {
            RefreshToken = initialTokens.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var rotatedTokens = await firstRefresh.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(rotatedTokens);

        var replayedRefresh = await client.PostAsJsonAsync("/agent/token/refresh", new
        {
            RefreshToken = initialTokens.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replayedRefresh.StatusCode);

        var familyRevokedRefresh = await client.PostAsJsonAsync("/agent/token/refresh", new
        {
            RefreshToken = rotatedTokens.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, familyRevokedRefresh.StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_CanProvisionAndRevokeAgent()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        var admin = await SeedUserAsync(factory, isPlatformAdministrator: true);
        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = admin.Email,
            Password = "Correct-Horse-2026!"
        });
        var adminTokens = await loginResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(adminTokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);

        var provisionResponse = await client.PostAsJsonAsync("/platform/agents", new
        {
            TenantId = seeded.Agent.TenantId,
            CompanyId = seeded.Agent.CompanyId,
            Name = "Worker 2"
        });

        Assert.Equal(HttpStatusCode.Created, provisionResponse.StatusCode);
        var provisioned = await provisionResponse.Content.ReadFromJsonAsync<ProvisionedAgentResponse>();
        Assert.NotNull(provisioned);
        Assert.False(string.IsNullOrWhiteSpace(provisioned.Secret));

        var revokeResponse = await client.PostAsync($"/platform/agents/{provisioned.AgentId}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
    }

    [Fact]
    public async Task CertifierConfiguration_IsStoredEncryptedAndServedToTheAgent()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        await SeedCertifierAssignmentAsync(factory, seeded.Agent.CompanyId);
        var admin = await SeedUserAsync(factory, isPlatformAdministrator: true);
        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = admin.Email,
            Password = "Correct-Horse-2026!"
        });
        var adminTokens = await loginResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(adminTokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);

        var putResponse = await client.PutAsJsonAsync(
            $"/platform/companies/{seeded.Agent.CompanyId}/certifier-credentials",
            new
            {
                Credentials = new Dictionary<string, string> { ["usuario"] = "worker-gt", ["llaveApi"] = "super-secret" },
                UseProduction = true
            });
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var handshakeResponse = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            Secret = seeded.Secret,
            WorkerVersion = "1.2.3"
        });
        var agentTokens = await handshakeResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(agentTokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentTokens.AccessToken);

        var configurationResponse = await client.GetAsync("/agent/certifier-configuration");

        Assert.Equal(HttpStatusCode.OK, configurationResponse.StatusCode);
        var configuration = await configurationResponse.Content.ReadFromJsonAsync<CertifierConfigurationResponse>();
        Assert.NotNull(configuration);
        Assert.Equal("INFILE", configuration.CertifierCode);
        Assert.Equal("GT", configuration.CountryCode);
        Assert.Equal("https://certifier.example/gt", configuration.EndpointUrl);
        Assert.Equal(CertifierRequestFormat.Xml, configuration.RequestFormat);
        Assert.True(configuration.UseProduction);
        Assert.Equal("worker-gt", configuration.Credentials["usuario"]);
        Assert.Equal("super-secret", configuration.Credentials["llaveApi"]);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var stored = await dbContext.CompanyCertifierAssignments
            .SingleAsync(x => x.CompanyId == seeded.Agent.CompanyId);
        Assert.NotNull(stored.CredentialsEncrypted);
        Assert.DoesNotContain("super-secret", Encoding.UTF8.GetString(stored.CredentialsEncrypted));
        Assert.Single(await dbContext.AuditLogs
            .Where(x => x.EventType == "certifier-credentials-updated")
            .ToListAsync());
    }

    [Fact]
    public async Task CertifierConfiguration_ReturnsConflictWhenCompanyHasNoAssignment()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        var handshakeResponse = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            Secret = seeded.Secret,
            WorkerVersion = "1.2.3"
        });
        var agentTokens = await handshakeResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(agentTokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentTokens.AccessToken);

        var configurationResponse = await client.GetAsync("/agent/certifier-configuration");

        Assert.Equal(HttpStatusCode.Conflict, configurationResponse.StatusCode);
    }

    internal static async Task SeedCertifierAssignmentAsync(ApiFactory factory, Guid companyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var certifier = new Certifier { Code = "INFILE", Name = "Infile" };
        var configuration = new CertifierCountryConfiguration
        {
            CertifierId = certifier.Id,
            CountryCode = "GT",
            EndpointUrl = "https://certifier.example/gt",
            RequestFormat = CertifierRequestFormat.Xml
        };
        dbContext.Certifiers.Add(certifier);
        dbContext.CertifierCountryConfigurations.Add(configuration);
        dbContext.CompanyCertifierAssignments.Add(new CompanyCertifierAssignment
        {
            CompanyId = companyId,
            CountryCode = "GT",
            CertifierCountryConfigurationId = configuration.Id
        });
        await dbContext.SaveChangesAsync();
    }

    internal static async Task<UserAccount> SeedUserAsync(ApiFactory factory, bool isPlatformAdministrator)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var user = new UserAccount
        {
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            DisplayName = "Platform Admin",
            IsActive = true
        };
        user.PasswordHash = new PasswordHasher<UserAccount>().HashPassword(user, "Correct-Horse-2026!");
        dbContext.UserAccounts.Add(user);
        if (isPlatformAdministrator)
        {
            dbContext.PlatformAdministrators.Add(new PlatformAdministrator { UserAccountId = user.Id });
        }

        await dbContext.SaveChangesAsync();
        return user;
    }

    internal static async Task<(Agent Agent, string Secret)> SeedAgentAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var tenant = new Tenant { Name = "Tenant" };
        var company = new Company
        {
            TenantId = tenant.Id,
            Name = "Company",
            NormalizedName = "COMPANY",
            CountryCode = "GT"
        };
        var plan = new SubscriptionPlan
        {
            Name = "Plan",
            NormalizedName = "PLAN",
            MaxCompanies = 5,
            MaxBranches = 10,
            MaxUsers = 5,
            MaxAgents = 2,
            GracePeriodDays = 5
        };
        dbContext.Tenants.Add(tenant);
        dbContext.Companies.Add(company);
        dbContext.SubscriptionPlans.Add(plan);
        dbContext.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            SubscriptionPlanId = plan.Id,
            ActivatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            IsCurrent = true
        });

        const string secret = "provisioned-agent-secret-for-tests";
        var agent = new Agent
        {
            TenantId = tenant.Id,
            CompanyId = company.Id,
            Name = "Worker 1",
            NormalizedName = "WORKER 1",
            CredentialHash = TokenHashing.Hash(secret)
        };
        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync();
        return (agent, secret);
    }

    internal sealed record AgentTokenResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);

    internal sealed record HeartbeatResponse(
        DateTimeOffset ServerTimeUtc,
        IReadOnlyList<PendingCommandResponse> PendingCommands);

    internal sealed record PendingCommandResponse(Guid Id, string Type, string Payload, DateTimeOffset LeaseExpiresAtUtc);
    private sealed record ProvisionedAgentResponse(Guid AgentId, string Secret);

    private sealed record CertifierConfigurationResponse(
        string CertifierCode,
        string CountryCode,
        string EndpointUrl,
        CertifierRequestFormat RequestFormat,
        bool UseProduction,
        IReadOnlyDictionary<string, string> Credentials);
}