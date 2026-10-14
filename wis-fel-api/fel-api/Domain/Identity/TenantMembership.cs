using fel_api.Domain.Tenants;

namespace fel_api.Domain.Identity;

public sealed class TenantMembership
{
    public Guid TenantId { get; set; }
    public Guid UserAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public UserAccount UserAccount { get; set; } = null!;
}