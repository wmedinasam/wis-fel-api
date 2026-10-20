using System.Text.Json.Serialization;
using fel_api.Domain.Catalog;

namespace fel_api.Domain.Documents;

// Status metadata reported by the Worker; the signed document never leaves the Worker.
public sealed class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid AgentId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? Series { get; set; }
    public string? Number { get; set; }
    public DocumentStatus Status { get; set; }
    public string? AuthorizationNumber { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset? CertifiedAtUtc { get; set; }
    public DateTimeOffset LastReportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResendRequestedAtUtc { get; set; }

    public Company Company { get; set; } = null!;
    public Branch? Branch { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<DocumentStatus>))]
public enum DocumentStatus
{
    Pending,
    Processing,
    Certified,
    Error,
    Retrying
}
