namespace fel_api.Domain.Catalog;

public sealed class CompanyCertifierAssignment
{
    public Guid CompanyId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public Guid CertifierCountryConfigurationId { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Company Company { get; set; } = null!;
    public CertifierCountryConfiguration CertifierCountryConfiguration { get; set; } = null!;
}