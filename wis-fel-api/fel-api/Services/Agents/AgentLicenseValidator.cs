using fel_api.Data;
using fel_api.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Agents;

public sealed class AgentLicenseValidator(FelApiDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<TenantSubscriptionState> ValidateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Include(x => x.SubscriptionPlan)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IsCurrent, cancellationToken);

        if (subscription is null || !subscription.SubscriptionPlan.IsActive)
        {
            throw new AgentLicenseInvalidException("No active subscription is configured for this tenant.");
        }

        var state = SubscriptionStateCalculator.Calculate(subscription, timeProvider.GetUtcNow());
        if (state == TenantSubscriptionState.Suspended)
        {
            throw new AgentLicenseInvalidException("The tenant subscription is suspended.");
        }

        return state;
    }
}

public sealed class AgentLicenseInvalidException(string message) : InvalidOperationException(message);