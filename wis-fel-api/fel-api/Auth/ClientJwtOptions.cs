namespace fel_api.Auth;

public sealed class ClientJwtOptions
{
    public const string SectionName = "ClientJwt";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;
}