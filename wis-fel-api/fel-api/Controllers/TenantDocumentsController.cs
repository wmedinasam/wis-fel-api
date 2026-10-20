using fel_api.Services.Auth;
using fel_api.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

// The IdP token carries no tenant, so the route tenant is only trusted after an active membership check.
[ApiController]
[Route("tenant/{tenantId:guid}/documents")]
[Authorize(AuthenticationSchemes = "ClientJwt")]
[EnableRateLimiting("tenant-documents")]
public sealed class TenantDocumentsController(
    ClientProfileService clientProfileService,
    DocumentQueryService documentQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DocumentPage>> List(
        Guid tenantId,
        [FromQuery] DocumentListQuery query,
        CancellationToken cancellationToken)
    {
        var companyIds = await clientProfileService.ResolveCompanyAccessAsync(User, tenantId, cancellationToken);
        return companyIds is null
            ? StatusCode(StatusCodes.Status403Forbidden)
            : Ok(await documentQueryService.ListAsync(query.ToFilter(tenantId), companyIds, cancellationToken));
    }

    [HttpGet("{documentId:guid}")]
    public async Task<ActionResult<DocumentResult>> Get(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var companyIds = await clientProfileService.ResolveCompanyAccessAsync(User, tenantId, cancellationToken);
        if (companyIds is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var document = await documentQueryService.GetAsync(documentId, tenantId, companyIds, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("{documentId:guid}/resend")]
    public async Task<IActionResult> Resend(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        var companyIds = await clientProfileService.ResolveCompanyAccessAsync(User, tenantId, cancellationToken);
        return companyIds is null
            ? StatusCode(StatusCodes.Status403Forbidden)
            : await DocumentResend.RequestAsync(this, documentQueryService, documentId, tenantId, companyIds, cancellationToken);
    }
}
