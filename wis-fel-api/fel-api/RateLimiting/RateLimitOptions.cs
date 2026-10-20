namespace fel_api.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowLimit PlatformLogin { get; set; } = new(10, 60);
    public FixedWindowLimit PlatformRefresh { get; set; } = new(20, 60);
    public FixedWindowLimit AgentHandshake { get; set; } = new(10, 60);
    public FixedWindowLimit AgentRefresh { get; set; } = new(20, 60);
    public FixedWindowLimit ClientMe { get; set; } = new(60, 60);
    public FixedWindowLimit AgentHeartbeat { get; set; } = new(120, 60);
    public FixedWindowLimit AgentDocumentStatus { get; set; } = new(60, 60);
    public FixedWindowLimit AgentConfiguration { get; set; } = new(60, 60);
    public FixedWindowLimit PlatformAgentManagement { get; set; } = new(30, 60);
    public FixedWindowLimit PlatformDocuments { get; set; } = new(60, 60);
    public FixedWindowLimit TenantDocuments { get; set; } = new(60, 60);
}

public sealed class FixedWindowLimit
{
    public FixedWindowLimit()
    {
    }

    public FixedWindowLimit(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public bool IsValid => PermitLimit > 0 && WindowSeconds > 0;
}