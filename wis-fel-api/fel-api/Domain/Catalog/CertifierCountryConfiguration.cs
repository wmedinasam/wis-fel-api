namespace fel_api.Domain.Catalog;

public sealed class CertifierCountryConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CertifierId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public CertifierRequestFormat RequestFormat { get; set; }
    public bool IsActive { get; set; } = true;

    public Certifier Certifier { get; set; } = null!;
}

public enum CertifierRequestFormat
{
    Json,
    Xml
}