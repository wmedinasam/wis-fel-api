namespace fel_api.Domain.Identity;

public sealed class PlatformAdministrator
{
    public Guid UserAccountId { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public UserAccount UserAccount { get; set; } = null!;
}