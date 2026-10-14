namespace fel_api.Domain.Subscriptions;

public static class SubscriptionStateCalculator
{
    public static TenantSubscriptionState Calculate(TenantSubscription subscription, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var plan = subscription.SubscriptionPlan;
        if (plan.GracePeriodDays < 0)
        {
            throw new InvalidOperationException("Grace period days cannot be negative.");
        }

        if (subscription.ExpiresAtUtc <= subscription.ActivatedAtUtc)
        {
            throw new InvalidOperationException("Subscription expiration must be after activation.");
        }

        if (nowUtc >= subscription.ExpiresAtUtc)
        {
            return TenantSubscriptionState.Suspended;
        }

        if (plan.GracePeriodDays == 0)
        {
            return TenantSubscriptionState.Active;
        }

        var graceStartsAtUtc = subscription.ExpiresAtUtc.AddDays(-plan.GracePeriodDays);
        if (graceStartsAtUtc < subscription.ActivatedAtUtc)
        {
            graceStartsAtUtc = subscription.ActivatedAtUtc;
        }

        return nowUtc >= graceStartsAtUtc
            ? TenantSubscriptionState.Grace
            : TenantSubscriptionState.Active;
    }
}