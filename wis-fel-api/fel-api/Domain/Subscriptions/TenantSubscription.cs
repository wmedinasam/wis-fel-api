using System.Text.Json.Serialization;
using fel_api.Domain.Tenants;

namespace fel_api.Domain.Subscriptions;

public sealed class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsCurrent { get; set; } = true;

    public Tenant Tenant { get; set; } = null!;
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
}

// El worker lee licenseState como texto: sin este converter saldría como 0/1/2.
[JsonConverter(typeof(JsonStringEnumConverter<TenantSubscriptionState>))]
public enum TenantSubscriptionState
{
    Active,
    Grace,
    Suspended
}