using System.Security.Claims;
using fel_api.Data;
using fel_api.Domain.Catalog;
using fel_api.Domain.Identity;
using fel_api.Domain.Tenants;
using fel_api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace fel_api.Tests;

public sealed class ClientProfileServiceTests
{
    [Fact]
    public async Task GetForPrincipalAsync_ReturnsOnlyMappedUsersTenantRolesAndCompanies()
    {
        await using var dbContext = new FelApiDbContext(
            new DbContextOptionsBuilder<FelApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var tenant = new Tenant { Name = "Tenant" };
        var user = new UserAccount
        {
            Email = "client@example.com",
            NormalizedEmail = "CLIENT@EXAMPLE.COM",
            DisplayName = "Client"
        };
        var company = new Company
        {
            TenantId = tenant.Id,
            Name = "Company",
            NormalizedName = "COMPANY",
            CountryCode = "GT"
        };
        var role = new TenantRole
        {
            TenantId = tenant.Id,
            Name = "Billing",
            NormalizedName = "BILLING"
        };
        dbContext.Tenants.Add(tenant);
        dbContext.UserAccounts.Add(user);
        dbContext.Companies.Add(company);
        dbContext.TenantRoles.Add(role);
        dbContext.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenant.Id,
            UserAccountId = user.Id
        });
        dbContext.TenantUserRoles.Add(new TenantUserRole
        {
            TenantId = tenant.Id,
            UserAccountId = user.Id,
            TenantRoleId = role.Id
        });
        dbContext.TenantRoleCompanies.Add(new TenantRoleCompany
        {
            TenantId = tenant.Id,
            TenantRoleId = role.Id,
            CompanyId = company.Id
        });
        dbContext.ExternalUserIdentities.Add(new ExternalUserIdentity
        {
            UserAccountId = user.Id,
            Issuer = "https://identity.example",
            Subject = "external-subject-123"
        });
        await dbContext.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://identity.example"),
            new Claim("sub", "external-subject-123")
        ],
        authenticationType: "ClientJwt"));
        var profileService = new ClientProfileService(dbContext);

        var result = await profileService.GetForPrincipalAsync(principal);

        Assert.NotNull(result);
        var tenantAccess = Assert.Single(result.Tenants);
        var roleAccess = Assert.Single(tenantAccess.Roles);
        var companyAccess = Assert.Single(roleAccess.Companies);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(tenant.Id, tenantAccess.Id);
        Assert.Equal(company.Id, companyAccess.Id);
    }
}