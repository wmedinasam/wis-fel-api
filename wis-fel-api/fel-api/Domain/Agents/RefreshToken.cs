using fel_api.Domain.Identity;

namespace fel_api.Domain.Agents;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public Guid? AgentId { get; set; }
    public Guid? UserAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public Agent? Agent { get; set; }
    public UserAccount? UserAccount { get; set; }
}