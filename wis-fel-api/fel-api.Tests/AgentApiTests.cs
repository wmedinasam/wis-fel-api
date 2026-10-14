using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
    public async Task Documents_PersistsPayloadAndFailsClosedWithoutMapper()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory, includeCertifier: true);
        var handshakeResponse = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            Secret = seeded.Secret,
            WorkerVersion = "1.2.3"
        });
        var tokens = await handshakeResponse.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent/documents")
        {
            Content = new StringContent("{\"workerPayload\":true}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", "doc-0001");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var document = await dbContext.DocumentSubmissions.SingleAsync();
        Assert.Equal(seeded.Agent.Id, document.AgentId);
        Assert.Equal("{\"workerPayload\":true}", document.IncomingPayload);
        Assert.Empty(await dbContext.DocumentDeliveryAttempts.ToListAsync());
    }

    private static async Task<UserAccount> SeedUserAsync(ApiFactory factory, bool isPlatformAdministrator)
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

    private static async Task<(Agent Agent, string Secret)> SeedAgentAsync(ApiFactory factory, bool includeCertifier = false)
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

        if (includeCertifier)
        {
            var certifier = new Certifier { Code = "INFILE", Name = "Infile" };
            var configuration = new CertifierCountryConfiguration
            {
                CertifierId = certifier.Id,
                CountryCode = company.CountryCode,
                EndpointUrl = "https://certifier.example/gt",
                RequestFormat = CertifierRequestFormat.Json
            };
            dbContext.Certifiers.Add(certifier);
            dbContext.CertifierCountryConfigurations.Add(configuration);
            dbContext.CompanyCertifierAssignments.Add(new CompanyCertifierAssignment
            {
                CompanyId = company.Id,
                CountryCode = company.CountryCode,
                CertifierCountryConfigurationId = configuration.Id
            });
        }

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

    private sealed record AgentTokenResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);

    private sealed record HeartbeatResponse(
        DateTimeOffset ServerTimeUtc,
        IReadOnlyList<PendingCommandResponse> PendingCommands);

    private sealed record PendingCommandResponse(Guid Id, string Type, string Payload, DateTimeOffset LeaseExpiresAtUtc);
    private sealed record ProvisionedAgentResponse(Guid AgentId, string Secret);
}