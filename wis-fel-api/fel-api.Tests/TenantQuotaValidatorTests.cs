using fel_api.Data;
using fel_api.Domain.Catalog;
using fel_api.Domain.Identity;
using fel_api.Domain.Subscriptions;
using fel_api.Domain.Tenants;
using fel_api.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace fel_api.Tests;

public sealed class TenantQuotaValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureCanAddCompanyAsync_ThrowsWhenCompanyLimitReached()
    {
        await using var dbContext = CreateContext();
        var (tenant, plan) = await AddSubscriptionAsync(dbContext, maxCompanies: 1);
        dbContext.Companies.Add(new Company
        {
            TenantId = tenant.Id,
            Name = "Existing",
            NormalizedName = "EXISTING",
            CountryCode = "GT"
        });
        await dbContext.SaveChangesAsync();

        var validator = new TenantQuotaValidator(dbContext);

        var exception = await Assert.ThrowsAsync<SubscriptionLimitExceededException>(
            () => validator.EnsureCanAddCompanyAsync(tenant.Id, Now));

        Assert.Equal(TenantQuotaResource.Company, exception.Resource);
        Assert.Equal(1, exception.Limit);
    }

    [Fact]
    public async Task EnsureCanAddBranchAsync_AllowsCreationBelowLimit()
    {
        await using var dbContext = CreateContext();
        var (tenant, _) = await AddSubscriptionAsync(dbContext, maxBranches: 2);
        var company = new Company
        {
            TenantId = tenant.Id,
            Name = "Company",
            NormalizedName = "COMPANY",
            CountryCode = "GT"
        };
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();
        dbContext.Branches.Add(new Branch
        {
            TenantId = tenant.Id,
            CompanyId = company.Id,
            Name = "Existing",
            NormalizedName = "EXISTING"
        });
        await dbContext.SaveChangesAsync();

        var validator = new TenantQuotaValidator(dbContext);

        await validator.EnsureCanAddBranchAsync(tenant.Id, Now);
    }

    [Fact]
    public async Task EnsureCanAddUserAsync_ThrowsWhenUserLimitReached()
    {
        await using var dbContext = CreateContext();
        var (tenant, _) = await AddSubscriptionAsync(dbContext, maxUsers: 1);
        var user = new UserAccount
        {
            Email = "user@example.com",
            NormalizedEmail = "USER@EXAMPLE.COM",
            DisplayName = "User"
        };
        dbContext.UserAccounts.Add(user);
        dbContext.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenant.Id,
            UserAccountId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var validator = new TenantQuotaValidator(dbContext);

        var exception = await Assert.ThrowsAsync<SubscriptionLimitExceededException>(
            () => validator.EnsureCanAddUserAsync(tenant.Id, Now));

        Assert.Equal(TenantQuotaResource.User, exception.Resource);
    }

    [Fact]
    public async Task EnsureCanAddCompanyAsync_RejectsExpiredSubscription()
    {
        await using var dbContext = CreateContext();
        var (tenant, _) = await AddSubscriptionAsync(dbContext, expiresAtUtc: Now);

        var validator = new TenantQuotaValidator(dbContext);

        await Assert.ThrowsAsync<TenantSuspendedException>(
            () => validator.EnsureCanAddCompanyAsync(tenant.Id, Now));
    }

    private static FelApiDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FelApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(Tenant Tenant, SubscriptionPlan Plan)> AddSubscriptionAsync(
        FelApiDbContext dbContext,
        int maxCompanies = 10,
        int maxBranches = 10,
        int maxUsers = 10,
        DateTimeOffset? expiresAtUtc = null)
    {
        var tenant = new Tenant { Name = "Tenant" };
        var plan = new SubscriptionPlan
        {
            Name = "Plan",
            NormalizedName = "PLAN",
            MaxCompanies = maxCompanies,
            MaxBranches = maxBranches,
            MaxUsers = maxUsers,
            GracePeriodDays = 5
        };
        dbContext.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            SubscriptionPlanId = plan.Id,
            ActivatedAtUtc = Now.AddDays(-30),
            ExpiresAtUtc = expiresAtUtc ?? Now.AddDays(30),
            IsCurrent = true
        });
        dbContext.Tenants.Add(tenant);
        dbContext.SubscriptionPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        return (tenant, plan);
    }
}