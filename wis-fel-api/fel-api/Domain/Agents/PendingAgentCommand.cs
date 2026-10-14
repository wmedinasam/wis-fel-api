namespace fel_api.Domain.Agents;

public sealed class PendingAgentCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }

    public Agent Agent { get; set; } = null!;
}