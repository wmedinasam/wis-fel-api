using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using fel_api.Domain.Subscriptions;
using fel_api.Services.Agents;
using fel_api.Services.Auth;
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
    DocumentSubmissionService documentSubmissionService) : ControllerBase
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
    public async Task<ActionResult<AuthTokenResponse>> Refresh(
        [FromBody] AgentRefreshRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await refreshTokenService.RotateForAgentAsync(request.RefreshToken, cancellationToken);
            return tokens is null ? Unauthorized() : Ok(AuthTokenResponse.From(tokens));
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

    [HttpPost("documents")]
    [Authorize(Policy = "Agent")]
    [EnableRateLimiting("agent-documents")]
    public async Task<IActionResult> SubmitDocument(
        [FromBody] JsonElement payload,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Branch-Id")] Guid? branchId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("agent_id"), out var agentId))
        {
            return Forbid();
        }

        try
        {
            var result = await documentSubmissionService.SubmitAsync(
                agentId,
                idempotencyKey,
                branchId,
                payload,
                cancellationToken);
            return StatusCode(result.HttpStatusCode, new
            {
                result.DocumentId,
                result.Delivered
            });
        }
        catch (CertifierRequestMapperNotConfiguredException exception)
        {
            var problem = new ProblemDetails
            {
                Title = "Certifier request mapping is not configured",
                Detail = "The request was stored but was not sent to the certifier.",
                Status = StatusCodes.Status503ServiceUnavailable
            };
            problem.Extensions["documentId"] = exception.DocumentId;
            return StatusCode(StatusCodes.Status503ServiceUnavailable, problem);
        }
        catch (DuplicateDocumentSubmissionException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate document submission",
                Detail = "The Idempotency-Key has already been used by this tenant.",
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (AgentBranchNotFoundException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Branch is outside the Agent company",
                Status = StatusCodes.Status400BadRequest
            });
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