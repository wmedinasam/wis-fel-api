using fel_api.Domain.Subscriptions;
using Xunit;

namespace fel_api.Tests;

public sealed class SubscriptionStateCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Calculate_ReturnsActiveBeforeGraceWindow()
    {
        var subscription = CreateSubscription(gracePeriodDays: 5, expiresAtUtc: Now.AddDays(10));

        var state = SubscriptionStateCalculator.Calculate(subscription, Now);

        Assert.Equal(TenantSubscriptionState.Active, state);
    }

    [Fact]
    public void Calculate_ReturnsGraceAtStartOfGraceWindow()
    {
        var subscription = CreateSubscription(gracePeriodDays: 5, expiresAtUtc: Now.AddDays(5));

        var state = SubscriptionStateCalculator.Calculate(subscription, Now);

        Assert.Equal(TenantSubscriptionState.Grace, state);
    }

    [Fact]
    public void Calculate_ReturnsSuspendedAtExpiration()
    {
        var subscription = CreateSubscription(gracePeriodDays: 5, expiresAtUtc: Now);

        var state = SubscriptionStateCalculator.Calculate(subscription, Now);

        Assert.Equal(TenantSubscriptionState.Suspended, state);
    }

    private static TenantSubscription CreateSubscription(int gracePeriodDays, DateTimeOffset expiresAtUtc) => new()
    {
        ActivatedAtUtc = Now.AddDays(-30),
        ExpiresAtUtc = expiresAtUtc,
        SubscriptionPlan = new SubscriptionPlan { GracePeriodDays = gracePeriodDays }
    };
}