namespace fel_api.Domain.Identity;

public sealed class TenantUserRole
{
    public Guid TenantId { get; set; }
    public Guid UserAccountId { get; set; }
    public Guid TenantRoleId { get; set; }

    public TenantMembership Membership { get; set; } = null!;
    public TenantRole TenantRole { get; set; } = null!;
}