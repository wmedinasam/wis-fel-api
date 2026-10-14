using fel_api.Domain.Tenants;

namespace fel_api.Domain.Identity;

public sealed class TenantRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Tenant Tenant { get; set; } = null!;
}