using System.Net.Http.Headers;
using System.Net.Http.Json;
using fel_api.Data;
using fel_api.Domain.Identity;
using fel_api.Domain.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static fel_api.Tests.AgentApiTests;

namespace fel_api.Tests;

public sealed class DevelopmentSeedTests
{
    [Fact]
    public async Task SeededData_IsReadableThroughGetEndpoints()
    {
        await using var factory = new ApiFactory();
        Guid tenantId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FelApiDbContext>();
            await DevelopmentDataSeeder.SeedAsync(
                db,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>(),
                ApiFactory.ClientIssuer);
            // Running it again must not duplicate data.
            await DevelopmentDataSeeder.SeedAsync(
                db,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>(),
                ApiFactory.ClientIssuer);
            tenantId = await db.Tenants.Select(x => x.Id).SingleAsync();
        }

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { Email = DevelopmentDataSeeder.AdminEmail, Password = DevelopmentDataSeeder.AdminPassword });
        var tokens = await login.Content.ReadFromJsonAsync<AgentTokenResponse>();
        Assert.NotNull(tokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var platform = await client.GetFromJsonAsync<SeededPage>("/platform/documents");
        Assert.NotNull(platform);
        Assert.Equal(6, platform.TotalCount);

        var route = $"/tenant/{tenantId}/documents";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DocumentApiTests.ClientToken(DevelopmentDataSeeder.OwnerSubject));
        var owner = await client.GetFromJsonAsync<SeededPage>(route);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DocumentApiTests.ClientToken(DevelopmentDataSeeder.RestrictedSubject));
        var restricted = await client.GetFromJsonAsync<SeededPage>(route);
        Assert.Equal(6, owner!.TotalCount);
        Assert.Equal(2, restricted!.TotalCount);
    }

    private sealed record SeededPage(int TotalCount);
}
