using System.ComponentModel.DataAnnotations;
using fel_api.Services.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

[ApiController]
[Route("platform/agents")]
[Authorize(Policy = "PlatformAdministrator")]
[EnableRateLimiting("platform-agent-management")]
public sealed class PlatformAgentsController(AgentProvisioningService provisioningService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProvisionAgentResponse>> Provision(
        [FromBody] ProvisionAgentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var provisionedAgent = await provisioningService.ProvisionAsync(
                request.TenantId,
                request.CompanyId,
                request.Name,
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new ProvisionAgentResponse(
                provisionedAgent.AgentId,
                provisionedAgent.Secret));
        }
        catch (AgentLimitExceededException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Agent limit reached",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (AgentLicenseInvalidException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Tenant is not licensed for Agent provisioning",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (AgentCompanyNotFoundException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Company does not belong to tenant",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (AgentNameAlreadyExistsException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Agent name already exists",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    [HttpPost("{agentId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid agentId, CancellationToken cancellationToken)
    {
        try
        {
            await provisioningService.RevokeAsync(agentId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

public sealed class ProvisionAgentRequest
{
    [Required]
    public Guid TenantId { get; init; }

    [Required]
    public Guid CompanyId { get; init; }

    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
}

public sealed record ProvisionAgentResponse(Guid AgentId, string Secret);