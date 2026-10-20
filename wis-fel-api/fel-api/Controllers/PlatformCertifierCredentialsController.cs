using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using fel_api.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

[ApiController]
[Route("platform/companies/{companyId:guid}/certifier-credentials")]
[Authorize(Policy = "PlatformAdministrator")]
[EnableRateLimiting("platform-agent-management")]
public sealed class PlatformCertifierCredentialsController(CertifierCredentialService credentialService) : ControllerBase
{
    private const int MaxKeyLength = 100;
    private const int MaxValueLength = 4000;

    [HttpPut]
    public async Task<IActionResult> SetCredentials(
        Guid companyId,
        [FromBody] SetCertifierCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        // DataAnnotations no valida el contenido de un diccionario.
        foreach (var (key, value) in request.Credentials)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength || (value?.Length ?? 0) > MaxValueLength)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid credential entry",
                    Detail = $"Credential keys must be 1 to {MaxKeyLength} characters and values at most {MaxValueLength} characters.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        try
        {
            await credentialService.SetCredentialsAsync(
                companyId,
                request.Credentials.ToDictionary(x => x.Key, x => x.Value ?? string.Empty),
                request.UseProduction,
                Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userAccountId) ? userAccountId : null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

public sealed class SetCertifierCredentialsRequest
{
    [Required, MinLength(1), MaxLength(20)]
    public Dictionary<string, string?> Credentials { get; init; } = [];

    public bool UseProduction { get; init; }
}
