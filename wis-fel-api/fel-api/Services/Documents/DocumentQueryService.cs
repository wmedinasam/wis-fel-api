using System.Text.Json;
using fel_api.Data;
using fel_api.Domain.Agents;
using fel_api.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Documents;

public sealed record DocumentFilter(
    Guid? TenantId,
    Guid? CompanyId,
    Guid? BranchId,
    DocumentStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

public sealed record DocumentResult(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid? BranchId,
    Guid AgentId,
    string IdempotencyKey,
    string DocumentType,
    string? Series,
    string? Number,
    DocumentStatus Status,
    string? AuthorizationNumber,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CertifiedAtUtc,
    DateTimeOffset LastReportedAtUtc,
    DateTimeOffset? ResendRequestedAtUtc);

public sealed record DocumentPage(IReadOnlyList<DocumentResult> Items, int Page, int PageSize, int TotalCount);

public sealed class DocumentQueryService(FelApiDbContext dbContext, TimeProvider timeProvider)
{
    public const string ResendCommandType = "ResendDocument";

    /// <param name="companyIds">Companies the caller may see; null means no company restriction.</param>
    public async Task<DocumentPage> ListAsync(
        DocumentFilter filter,
        IReadOnlyCollection<Guid>? companyIds,
        CancellationToken cancellationToken = default)
    {
        var query = Scope(filter.TenantId, companyIds);
        if (filter.CompanyId is not null) query = query.Where(x => x.CompanyId == filter.CompanyId);
        if (filter.BranchId is not null) query = query.Where(x => x.BranchId == filter.BranchId);
        if (filter.Status is not null) query = query.Where(x => x.Status == filter.Status);
        if (filter.FromUtc is not null) query = query.Where(x => x.IssuedAtUtc >= filter.FromUtc);
        if (filter.ToUtc is not null) query = query.Where(x => x.IssuedAtUtc < filter.ToUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.IssuedAtUtc)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(ToResult)
            .ToListAsync(cancellationToken);
        return new DocumentPage(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<DocumentResult?> GetAsync(
        Guid documentId,
        Guid? tenantId,
        IReadOnlyCollection<Guid>? companyIds,
        CancellationToken cancellationToken = default) =>
        await Scope(tenantId, companyIds)
            .Where(x => x.Id == documentId)
            .Select(ToResult)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Queues a ResendDocument command for the Agent that reported the document.</summary>
    public async Task RequestResendAsync(
        Guid documentId,
        Guid? tenantId,
        IReadOnlyCollection<Guid>? companyIds,
        CancellationToken cancellationToken = default)
    {
        var document = await Scope(tenantId, companyIds)
            .SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document '{documentId}' was not found.");
        if (document.Status == DocumentStatus.Certified)
        {
            throw new DocumentResendNotAllowedException("The document is already certified.");
        }

        if (document.ResendRequestedAtUtc is not null)
        {
            throw new DocumentResendNotAllowedException("A resend is already pending for this document.");
        }

        if (!await dbContext.Agents.AnyAsync(x => x.Id == document.AgentId && x.IsActive, cancellationToken))
        {
            throw new DocumentResendNotAllowedException("The Agent that reported the document is not active.");
        }

        var now = timeProvider.GetUtcNow();
        document.ResendRequestedAtUtc = now;
        dbContext.PendingAgentCommands.Add(new PendingAgentCommand
        {
            AgentId = document.AgentId,
            CommandType = ResendCommandType,
            Payload = JsonSerializer.Serialize(
                new { documentId = document.Id, idempotencyKey = document.IdempotencyKey }),
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Document> Scope(Guid? tenantId, IReadOnlyCollection<Guid>? companyIds)
    {
        var query = dbContext.Documents.AsQueryable();
        if (tenantId is not null) query = query.Where(x => x.TenantId == tenantId);
        if (companyIds is not null) query = query.Where(x => companyIds.Contains(x.CompanyId));
        return query;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Document, DocumentResult>> ToResult = x => new DocumentResult(
        x.Id,
        x.TenantId,
        x.CompanyId,
        x.BranchId,
        x.AgentId,
        x.IdempotencyKey,
        x.DocumentType,
        x.Series,
        x.Number,
        x.Status,
        x.AuthorizationNumber,
        x.ErrorCode,
        x.ErrorMessage,
        x.AttemptCount,
        x.IssuedAtUtc,
        x.CertifiedAtUtc,
        x.LastReportedAtUtc,
        x.ResendRequestedAtUtc);
}

public sealed class DocumentResendNotAllowedException(string message) : InvalidOperationException(message);
