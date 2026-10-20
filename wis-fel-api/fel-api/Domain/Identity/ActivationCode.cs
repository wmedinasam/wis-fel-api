namespace fel_api.Domain.Identity;

public sealed class ActivationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public Guid? UsedByAgentId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
