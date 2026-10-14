using fel_api.Data;
using fel_api.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Subscriptions;

public enum TenantQuotaResource
{
    Company,
    Branch,
    User
}

public sealed class TenantQuotaValidator(FelApiDbContext dbContext)
{
    public Task EnsureCanAddCompanyAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        EnsureCanAddAsync(tenantId, TenantQuotaResource.Company, nowUtc, cancellationToken);

    public Task EnsureCanAddBranchAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        EnsureCanAddAsync(tenantId, TenantQuotaResource.Branch, nowUtc, cancellationToken);

    public Task EnsureCanAddUserAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        EnsureCanAddAsync(tenantId, TenantQuotaResource.User, nowUtc, cancellationToken);

    private async Task EnsureCanAddAsync(
        Guid tenantId,
        TenantQuotaResource resource,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Include(x => x.SubscriptionPlan)
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.IsCurrent,
                cancellationToken);

        if (subscription is null || !subscription.SubscriptionPlan.IsActive)
        {
            throw new TenantSubscriptionRequiredException(tenantId);
        }

        var state = SubscriptionStateCalculator.Calculate(subscription, nowUtc);
        if (state == TenantSubscriptionState.Suspended)
        {
            throw new TenantSuspendedException(tenantId);
        }

        var (used, limit) = resource switch
        {
            TenantQuotaResource.Company => (
                await dbContext.Companies.CountAsync(x => x.TenantId == tenantId, cancellationToken),
                subscription.SubscriptionPlan.MaxCompanies),
            TenantQuotaResource.Branch => (
                await dbContext.Branches.CountAsync(x => x.TenantId == tenantId, cancellationToken),
                subscription.SubscriptionPlan.MaxBranches),
            TenantQuotaResource.User => (
                await dbContext.TenantMemberships.CountAsync(
                    x => x.TenantId == tenantId && x.IsActive && x.UserAccount.IsActive,
                    cancellationToken),
                subscription.SubscriptionPlan.MaxUsers),
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null)
        };

        if (used >= limit)
        {
            throw new SubscriptionLimitExceededException(tenantId, resource, limit);
        }
    }
}

public sealed class TenantSubscriptionRequiredException(Guid tenantId)
    : InvalidOperationException($"Tenant '{tenantId}' has no active subscription plan.");

public sealed class TenantSuspendedException(Guid tenantId)
    : InvalidOperationException($"Tenant '{tenantId}' has an expired subscription.");

public sealed class SubscriptionLimitExceededException(
    Guid tenantId,
    TenantQuotaResource resource,
    int limit)
    : InvalidOperationException($"Tenant '{tenantId}' has reached its {resource} limit of {limit}.")
{
    public Guid TenantId { get; } = tenantId;
    public TenantQuotaResource Resource { get; } = resource;
    public int Limit { get; } = limit;
}