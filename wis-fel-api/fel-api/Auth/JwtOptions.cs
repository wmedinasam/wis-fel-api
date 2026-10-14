namespace fel_api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public string PlatformAudience { get; set; } = string.Empty;
    public string AgentAudience { get; set; } = string.Empty;
    public int PlatformTokenMinutes { get; set; } = 15;
    public int AgentTokenMinutes { get; set; } = 15;
    public int PlatformRefreshTokenDays { get; set; } = 7;
    public int AgentRefreshTokenDays { get; set; } = 30;
}