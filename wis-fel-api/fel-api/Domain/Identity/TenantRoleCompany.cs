using fel_api.Domain.Catalog;

namespace fel_api.Domain.Identity;

public sealed class TenantRoleCompany
{
    public Guid TenantId { get; set; }
    public Guid TenantRoleId { get; set; }
    public Guid CompanyId { get; set; }

    public TenantRole TenantRole { get; set; } = null!;
    public Company Company { get; set; } = null!;
}