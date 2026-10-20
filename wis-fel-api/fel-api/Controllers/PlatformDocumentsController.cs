using System.ComponentModel.DataAnnotations;
using fel_api.Domain.Documents;
using fel_api.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

[ApiController]
[Route("platform/documents")]
[Authorize(Policy = "PlatformAdministrator")]
[EnableRateLimiting("platform-documents")]
public sealed class PlatformDocumentsController(DocumentQueryService documentQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DocumentPage>> List(
        [FromQuery] DocumentListQuery query,
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken) =>
        Ok(await documentQueryService.ListAsync(query.ToFilter(tenantId), null, cancellationToken));

    [HttpGet("{documentId:guid}")]
    public async Task<ActionResult<DocumentResult>> Get(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documentQueryService.GetAsync(documentId, null, null, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("{documentId:guid}/resend")]
    public Task<IActionResult> Resend(Guid documentId, CancellationToken cancellationToken) =>
        DocumentResend.RequestAsync(this, documentQueryService, documentId, null, null, cancellationToken);
}

public sealed class DocumentListQuery
{
    public Guid? CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public DocumentStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 50;

    public DocumentFilter ToFilter(Guid? tenantId) =>
        new(tenantId, CompanyId, BranchId, Status, From, To, Page, PageSize);
}

internal static class DocumentResend
{
    public static async Task<IActionResult> RequestAsync(
        ControllerBase controller,
        DocumentQueryService documentQueryService,
        Guid documentId,
        Guid? tenantId,
        IReadOnlyCollection<Guid>? companyIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await documentQueryService.RequestResendAsync(documentId, tenantId, companyIds, cancellationToken);
            return controller.Accepted();
        }
        catch (KeyNotFoundException)
        {
            return controller.NotFound();
        }
        catch (DocumentResendNotAllowedException exception)
        {
            return controller.Conflict(new ProblemDetails
            {
                Title = "Document cannot be resent",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }
}
