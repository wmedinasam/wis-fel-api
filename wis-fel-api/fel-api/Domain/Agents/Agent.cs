using fel_api.Domain.Catalog;

namespace fel_api.Domain.Agents;

public sealed class Agent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string CredentialHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? MachineHostname { get; set; }
    public string? MachineId { get; set; }
    public string? MachineOs { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHandshakeAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public string? WorkerVersion { get; set; }

    public Company Company { get; set; } = null!;
}