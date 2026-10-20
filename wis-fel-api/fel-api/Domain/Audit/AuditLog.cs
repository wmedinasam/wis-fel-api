namespace fel_api.Domain.Audit;

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserAccountId { get; set; }
    public Guid? AgentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
