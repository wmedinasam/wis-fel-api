using fel_api.Data;
using fel_api.Domain.Documents;
using fel_api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Documents;

public sealed record DocumentStatusReport(
    string IdempotencyKey,
    Guid? BranchId,
    string DocumentType,
    string? Series,
    string? Number,
    DocumentStatus Status,
    string? AuthorizationNumber,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CertifiedAtUtc);

public sealed record RejectedDocumentReport(string IdempotencyKey, string Reason);

public sealed record DocumentStatusReportResult(int Accepted, IReadOnlyList<RejectedDocumentReport> Rejected);

public sealed class DocumentStatusService(
    FelApiDbContext dbContext,
    AgentLicenseValidator licenseValidator,
    TimeProvider timeProvider)
{
    public async Task<DocumentStatusReportResult> ReportAsync(
        Guid agentId,
        IReadOnlyList<DocumentStatusReport> reports,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == agentId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found or is inactive.");
        await licenseValidator.ValidateAsync(agent.TenantId, cancellationToken);

        var keys = reports.Select(x => x.IdempotencyKey.Trim()).Distinct().ToArray();
        var branchIds = reports.Where(x => x.BranchId is not null).Select(x => x.BranchId!.Value).Distinct().ToArray();
        var validBranchIds = await dbContext.Branches
            .Where(x => branchIds.Contains(x.Id) && x.TenantId == agent.TenantId && x.CompanyId == agent.CompanyId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var existing = await dbContext.Documents
            .Where(x => x.TenantId == agent.TenantId && keys.Contains(x.IdempotencyKey))
            .ToDictionaryAsync(x => x.IdempotencyKey, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var rejected = new List<RejectedDocumentReport>();
        var accepted = 0;
        foreach (var report in reports)
        {
            var key = report.IdempotencyKey.Trim();
            if (report.BranchId is not null && !validBranchIds.Contains(report.BranchId.Value))
            {
                rejected.Add(new RejectedDocumentReport(key, "Branch is outside the Agent company."));
                continue;
            }

            if (!existing.TryGetValue(key, out var document))
            {
                document = new Document
                {
                    TenantId = agent.TenantId,
                    CompanyId = agent.CompanyId,
                    IdempotencyKey = key
                };
                dbContext.Documents.Add(document);
                existing[key] = document;
            }
            else if (document.CompanyId != agent.CompanyId)
            {
                rejected.Add(new RejectedDocumentReport(key, "Document belongs to another company."));
                continue;
            }

            document.AgentId = agent.Id;
            document.BranchId = report.BranchId;
            document.DocumentType = report.DocumentType.Trim();
            document.Series = report.Series;
            document.Number = report.Number;
            document.Status = report.Status;
            document.AuthorizationNumber = report.AuthorizationNumber;
            document.ErrorCode = report.ErrorCode;
            document.ErrorMessage = report.ErrorMessage;
            document.AttemptCount = report.AttemptCount;
            document.IssuedAtUtc = report.IssuedAtUtc;
            document.CertifiedAtUtc = report.CertifiedAtUtc;
            document.LastReportedAtUtc = now;
            if (report.Status is DocumentStatus.Certified or DocumentStatus.Error)
            {
                document.ResendRequestedAtUtc = null;
            }

            accepted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DocumentStatusReportResult(accepted, rejected);
    }
}
