using fel_api.Domain.Tenants;

namespace fel_api.Domain.Catalog;

public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string? TaxId { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Timezone { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}