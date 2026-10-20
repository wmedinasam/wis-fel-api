using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using fel_api.Domain.Catalog;
using fel_api.Domain.Documents;
using fel_api.Domain.Subscriptions;
using fel_api.Services.Agents;
using fel_api.Services.Auth;
using fel_api.Services.Catalog;
using fel_api.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

[ApiController]
[Route("agent")]
public sealed class AgentController(
    AgentHandshakeService handshakeService,
    AgentHeartbeatService heartbeatService,
    RefreshTokenService refreshTokenService,
    DocumentStatusService documentStatusService,
    AgentLicenseValidator licenseValidator,
    CertifierConfigurationResolver certifierConfigurationResolver) : ControllerBase
{
    [HttpPost("handshake")]
    [AllowAnonymous]
    [EnableRateLimiting("agent-handshake")]
    public async Task<ActionResult<AgentHandshakeResponse>> Handshake(
        [FromBody] AgentHandshakeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handshakeService.HandshakeAsync(
                request.AgentId,
                request.Secret,
                request.WorkerVersion,
                cancellationToken);
            return result is null
                ? Unauthorized()
                : Ok(AgentHandshakeResponse.From(result));
        }
        catch (AgentLicenseInvalidException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Agent license is not valid",
                Detail = exception.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
    }

    [HttpPost("token/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("agent-refresh")]
    public async Task<ActionResult<AgentRefreshResponse>> Refresh(
        [FromBody] AgentRefreshRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rotation = await refreshTokenService.RotateForAgentAsync(request.RefreshToken, cancellationToken);
            return rotation is null ? Unauthorized() : Ok(AgentRefreshResponse.From(rotation));
        }
        catch (AgentLicenseInvalidException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("heartbeat")]
    [Authorize(Policy = "Agent")]
    [EnableRateLimiting("agent-heartbeat")]
    public async Task<ActionResult<AgentHeartbeatResult>> Heartbeat(
        [FromBody] AgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("agent_id"), out var agentId))
        {
            return Forbid();
        }

        try
        {
            var result = await heartbeatService.HeartbeatAsync(
                agentId,
                request.WorkerVersion,
                request.AcknowledgedCommandIds ?? [],
                cancellationToken);
            return Ok(result);
        }
        catch (AgentLicenseInvalidException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("certifier-configuration")]
    [Authorize(Policy = "Agent")]
    [EnableRateLimiting("agent-configuration")]
    public async Task<ActionResult<AgentCertifierConfigurationResponse>> GetCertifierConfiguration(
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("agent_id"), out var agentId)
            || !Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId)
            || !Guid.TryParse(User.FindFirstValue("company_id"), out var companyId))
        {
            return Forbid();
        }

        try
        {
            await licenseValidator.ValidateAsync(tenantId, cancellationToken);
            var configuration = await certifierConfigurationResolver.ResolveForActiveAgentAsync(
                agentId,
                tenantId,
                companyId,
                cancellationToken);
            if (configuration is null)
            {
                // Revoked after the access token was issued: the token is still valid for minutes, the secrets are not.
                return Unauthorized();
            }

            return Ok(new AgentCertifierConfigurationResponse(
                configuration.CertifierCode,
                configuration.CountryCode,
                configuration.EndpointUrl,
                configuration.RequestFormat,
                configuration.UseProduction,
                configuration.Credentials));
        }
        catch (AgentLicenseInvalidException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Agent license is not valid",
                Detail = exception.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Certifier configuration is not usable",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (CryptographicException)
        {
            // Las credenciales se cifran con el key ring de Data Protection: si se perdió, no hay forma de recuperarlas.
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Certifier credentials could not be decrypted",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    [HttpPost("documents/status")]
    [Authorize(Policy = "Agent")]
    [EnableRateLimiting("agent-document-status")]
    public async Task<ActionResult<DocumentStatusReportResult>> ReportDocumentStatus(
        [FromBody] DocumentStatusBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("agent_id"), out var agentId))
        {
            return Forbid();
        }

        try
        {
            var result = await documentStatusService.ReportAsync(
                agentId,
                request.Documents.Select(x => new DocumentStatusReport(
                    x.IdempotencyKey,
                    x.BranchId,
                    x.DocumentType,
                    x.Series,
                    x.Number,
                    x.Status!.Value,
                    x.AuthorizationNumber,
                    x.ErrorCode,
                    x.ErrorMessage,
                    x.AttemptCount,
                    x.IssuedAtUtc!.Value,
                    x.CertifiedAtUtc)).ToArray(),
                cancellationToken);
            return Ok(result);
        }
        catch (AgentLicenseInvalidException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized();
        }
    }
}

public sealed class AgentHandshakeRequest
{
    [Required]
    public Guid AgentId { get; init; }

    [Required, StringLength(128, MinimumLength = 32)]
    public string Secret { get; init; } = string.Empty;

    [Required, StringLength(80)]
    public string WorkerVersion { get; init; } = string.Empty;
}

public sealed record AgentHandshakeResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    TenantSubscriptionState LicenseState,
    DateTimeOffset HandshakenAtUtc,
    Guid TenantId,
    Guid CompanyId)
{
    public static AgentHandshakeResponse From(AgentHandshakeResult result) => new(
        result.Tokens.AccessToken,
        "Bearer",
        result.Tokens.AccessTokenExpiresAtUtc,
        result.Tokens.RefreshToken,
        result.Tokens.RefreshTokenExpiresAtUtc,
        result.SubscriptionState,
        result.HandshakenAtUtc,
        result.TenantId,
        result.CompanyId);
}

/// <summary>Refresh response for the worker: the tokens plus the license state the worker syncs on.</summary>
public sealed record AgentRefreshResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    TenantSubscriptionState? LicenseState)
{
    public static AgentRefreshResponse From(AgentRotation rotation) => new(
        rotation.Tokens.AccessToken,
        "Bearer",
        rotation.Tokens.AccessTokenExpiresAtUtc,
        rotation.Tokens.RefreshToken,
        rotation.Tokens.RefreshTokenExpiresAtUtc,
        rotation.LicenseState);
}

public sealed record AgentCertifierConfigurationResponse(
    string CertifierCode,
    string CountryCode,
    string EndpointUrl,
    CertifierRequestFormat RequestFormat,
    bool UseProduction,
    IReadOnlyDictionary<string, string> Credentials);

public sealed class AgentRefreshRequest
{
    [Required, StringLength(128, MinimumLength = 32)]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class AgentHeartbeatRequest
{
    [Required, StringLength(80)]
    public string WorkerVersion { get; init; } = string.Empty;

    public IReadOnlyCollection<Guid>? AcknowledgedCommandIds { get; init; }
}
public sealed class DocumentStatusBatchRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<DocumentStatusItemRequest> Documents { get; init; } = [];
}

public sealed class DocumentStatusItemRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public Guid? BranchId { get; init; }

    [Required, StringLength(20, MinimumLength = 1)]
    public string DocumentType { get; init; } = string.Empty;

    [StringLength(50)]
    public string? Series { get; init; }

    [StringLength(50)]
    public string? Number { get; init; }

    [Required]
    public DocumentStatus? Status { get; init; }

    [StringLength(100)]
    public string? AuthorizationNumber { get; init; }

    [StringLength(100)]
    public string? ErrorCode { get; init; }

    [StringLength(4000)]
    public string? ErrorMessage { get; init; }

    [Range(0, int.MaxValue)]
    public int AttemptCount { get; init; }

    [Required]
    public DateTimeOffset? IssuedAtUtc { get; init; }

    public DateTimeOffset? CertifiedAtUtc { get; init; }
}
