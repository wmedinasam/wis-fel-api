namespace fel_api.Domain.Identity;

public sealed class ExternalUserIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    public UserAccount UserAccount { get; set; } = null!;
}