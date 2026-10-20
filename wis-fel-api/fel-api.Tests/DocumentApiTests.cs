using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using fel_api.Data;
using fel_api.Domain.Agents;
using fel_api.Domain.Catalog;
using fel_api.Domain.Documents;
using fel_api.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static fel_api.Tests.AgentApiTests;

namespace fel_api.Tests;

public sealed class DocumentApiTests
{
    [Fact]
    public async Task StatusReport_UpsertsByIdempotencyKeyAndRejectsForeignBranch()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        await AuthorizeAgentAsync(client, seeded);
        var foreignBranchId = await SeedForeignBranchAsync(factory, seeded.Agent);

        var first = await client.PostAsJsonAsync("/agent/documents/status", Batch(
            Report("doc-1", "Error", errorCode: "E01"),
            Report("doc-2", "Pending", branchId: foreignBranchId)));
        var second = await client.PostAsJsonAsync("/agent/documents/status", Batch(
            Report("doc-1", "Certified", authorizationNumber: "AUTH-1")));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<StatusReportResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal(1, firstResult.Accepted);
        Assert.Equal("doc-2", Assert.Single(firstResult.Rejected).IdempotencyKey);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var document = await scope.ServiceProvider.GetRequiredService<FelApiDbContext>().Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Certified, document.Status);
        Assert.Equal("AUTH-1", document.AuthorizationNumber);
        Assert.Equal(seeded.Agent.Id, document.AgentId);
    }

    [Fact]
    public async Task PlatformResend_QueuesCommandDeliveredByHeartbeat()
    {
        await using var factory = new ApiFactory();
        using var agentClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        await AuthorizeAgentAsync(agentClient, seeded);
        await AuthorizeAdminAsync(factory, adminClient);
        await agentClient.PostAsJsonAsync("/agent/documents/status", Batch(
            Report("doc-err", "Error"),
            Report("doc-ok", "Certified")));

        var errors = await adminClient.GetFromJsonAsync<DocumentPageResponse>("/platform/documents?status=Error");
        Assert.NotNull(errors);
        var failed = Assert.Single(errors.Items);
        Assert.Equal("doc-err", failed.IdempotencyKey);
        var certified = await adminClient.GetFromJsonAsync<DocumentPageResponse>("/platform/documents?status=Certified");
        Assert.NotNull(certified);

        var resend = await adminClient.PostAsync($"/platform/documents/{failed.Id}/resend", null);
        var duplicateResend = await adminClient.PostAsync($"/platform/documents/{failed.Id}/resend", null);
        var certifiedResend = await adminClient.PostAsync($"/platform/documents/{certified.Items[0].Id}/resend", null);

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, certifiedResend.StatusCode);

        var heartbeat = await agentClient.PostAsJsonAsync("/agent/heartbeat", new
        {
            WorkerVersion = "1.2.3",
            AcknowledgedCommandIds = Array.Empty<Guid>()
        });
        var heartbeatResult = await heartbeat.Content.ReadFromJsonAsync<HeartbeatResponse>();
        Assert.NotNull(heartbeatResult);
        var command = Assert.Single(heartbeatResult.PendingCommands);
        Assert.Equal("ResendDocument", command.Type);
        Assert.Contains(failed.Id.ToString(), command.Payload);
    }

    [Fact]
    public async Task TenantDocuments_AreScopedByMembershipAndRoleCompanies()
    {
        await using var factory = new ApiFactory();
        using var agentClient = factory.CreateClient();
        using var tenantClient = factory.CreateClient();
        var seeded = await SeedAgentAsync(factory);
        await AuthorizeAgentAsync(agentClient, seeded);
        await agentClient.PostAsJsonAsync("/agent/documents/status", Batch(Report("doc-1", "Error")));
        var otherCompanyId = await SeedTenantUserAsync(factory, seeded.Agent.TenantId, "restricted", restrictToOtherCompany: true);
        await SeedTenantUserAsync(factory, seeded.Agent.TenantId, "unrestricted", restrictToOtherCompany: false);
        var route = $"/tenant/{seeded.Agent.TenantId}/documents";

        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ClientToken("restricted"));
        var restricted = await tenantClient.GetFromJsonAsync<DocumentPageResponse>(route);
        var foreignTenant = await tenantClient.GetAsync($"/tenant/{Guid.NewGuid()}/documents");
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ClientToken("unrestricted"));
        var unrestricted = await tenantClient.GetFromJsonAsync<DocumentPageResponse>(route);

        Assert.NotNull(restricted);
        Assert.Empty(restricted.Items);
        Assert.NotEqual(seeded.Agent.CompanyId, otherCompanyId);
        Assert.Equal(HttpStatusCode.Forbidden, foreignTenant.StatusCode);
        Assert.NotNull(unrestricted);
        var document = Assert.Single(unrestricted.Items);
        var resend = await tenantClient.PostAsync($"{route}/{document.Id}/resend", null);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
    }

    private static object Batch(params object[] documents) => new { Documents = documents };

    private static object Report(
        string key,
        string status,
        Guid? branchId = null,
        string? errorCode = null,
        string? authorizationNumber = null) => new
        {
            IdempotencyKey = key,
            BranchId = branchId,
            DocumentType = "FACT",
            Series = "A",
            Number = "1",
            Status = status,
            AuthorizationNumber = authorizationNumber,
            ErrorCode = errorCode,
            AttemptCount = 1,
            IssuedAtUtc = DateTimeOffset.UtcNow
        };

    private static async Task AuthorizeAgentAsync(HttpClient client, (Agent Agent, string Secret) seeded)
    {
        var response = await client.PostAsJsonAsync("/agent/handshake", new
        {
            AgentId = seeded.Agent.Id,
            seeded.Secret,
            WorkerVersion = "1.2.3"
        });
        var tokens = await response.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    private static async Task AuthorizeAdminAsync(ApiFactory factory, HttpClient client)
    {
        var admin = await SeedUserAsync(factory, isPlatformAdministrator: true);
        var response = await client.PostAsJsonAsync("/auth/login", new { admin.Email, Password = "Correct-Horse-2026!" });
        var tokens = await response.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    private static async Task<Guid> SeedForeignBranchAsync(ApiFactory factory, Agent agent)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var company = new Company { TenantId = agent.TenantId, Name = "Other", NormalizedName = "OTHER", CountryCode = "SV" };
        var branch = new Branch { TenantId = agent.TenantId, CompanyId = company.Id, Name = "B", NormalizedName = "B" };
        dbContext.Companies.Add(company);
        dbContext.Branches.Add(branch);
        await dbContext.SaveChangesAsync();
        return branch.Id;
    }

    // Seeds a tenant user whose role is either restricted to a different company or unrestricted.
    private static async Task<Guid> SeedTenantUserAsync(ApiFactory factory, Guid tenantId, string subject, bool restrictToOtherCompany)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
        var user = new UserAccount
        {
            Email = $"{subject}@example.com",
            NormalizedEmail = $"{subject.ToUpperInvariant()}@EXAMPLE.COM",
            DisplayName = subject,
            IsActive = true
        };
        var role = new TenantRole { TenantId = tenantId, Name = subject, NormalizedName = subject.ToUpperInvariant() };
        var otherCompany = new Company
        {
            TenantId = tenantId,
            Name = $"Other {subject}",
            NormalizedName = $"OTHER {subject.ToUpperInvariant()}",
            CountryCode = "SV"
        };
        dbContext.UserAccounts.Add(user);
        dbContext.ExternalUserIdentities.Add(new ExternalUserIdentity
        {
            UserAccountId = user.Id,
            Issuer = ApiFactory.ClientIssuer,
            Subject = subject
        });
        dbContext.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserAccountId = user.Id });
        dbContext.TenantRoles.Add(role);
        dbContext.TenantUserRoles.Add(new TenantUserRole { TenantId = tenantId, UserAccountId = user.Id, TenantRoleId = role.Id });
        dbContext.Companies.Add(otherCompany);
        if (restrictToOtherCompany)
        {
            dbContext.TenantRoleCompanies.Add(new TenantRoleCompany
            {
                TenantId = tenantId,
                TenantRoleId = role.Id,
                CompanyId = otherCompany.Id
            });
        }

        await dbContext.SaveChangesAsync();
        return otherCompany.Id;
    }

    private static string ClientToken(string subject) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        ApiFactory.ClientIssuer,
        ApiFactory.ClientAudience,
        [new Claim("sub", subject)],
        expires: DateTime.UtcNow.AddMinutes(5),
        signingCredentials: new SigningCredentials(ApiFactory.ClientSigningKey, SecurityAlgorithms.HmacSha256)));

    private sealed record StatusReportResponse(int Accepted, IReadOnlyList<RejectedResponse> Rejected);
    private sealed record RejectedResponse(string IdempotencyKey, string Reason);
    private sealed record DocumentPageResponse(IReadOnlyList<DocumentItemResponse> Items, int TotalCount);
    private sealed record DocumentItemResponse(Guid Id, string IdempotencyKey, DocumentStatus Status);
}
