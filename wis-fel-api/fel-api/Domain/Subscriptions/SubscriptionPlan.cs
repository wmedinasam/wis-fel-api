namespace fel_api.Domain.Subscriptions;

public sealed class SubscriptionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int MaxCompanies { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public int MaxAgents { get; set; } = 1;
    public int GracePeriodDays { get; set; }
    public bool IsActive { get; set; } = true;
}