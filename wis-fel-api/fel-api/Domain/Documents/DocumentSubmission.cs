using fel_api.Domain.Catalog;

namespace fel_api.Domain.Documents;

public sealed class DocumentSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? BranchId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string IncomingPayload { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Company Company { get; set; } = null!;
    public Branch? Branch { get; set; }
}