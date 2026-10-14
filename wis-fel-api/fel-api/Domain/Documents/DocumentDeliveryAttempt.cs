namespace fel_api.Domain.Documents;

public sealed class DocumentDeliveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentSubmissionId { get; set; }
    public int AttemptNumber { get; set; }
    public string RequestPayload { get; set; } = string.Empty;
    public string? ResponsePayload { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorDetails { get; set; }
    public bool? Succeeded { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DocumentSubmission DocumentSubmission { get; set; } = null!;
}