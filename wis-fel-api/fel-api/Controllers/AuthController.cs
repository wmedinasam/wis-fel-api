using System.ComponentModel.DataAnnotations;
using fel_api.Auth;
using fel_api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace fel_api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    PlatformAuthenticationService platformAuthenticationService,
    RefreshTokenService refreshTokenService,
    ClientProfileService clientProfileService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("platform-login")]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        [FromBody] PlatformLoginRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await platformAuthenticationService.LoginAsync(
            request.Email,
            request.Password,
            cancellationToken);
        return tokens is null ? Unauthorized() : Ok(AuthTokenResponse.From(tokens));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("platform-refresh")]
    public async Task<ActionResult<AuthTokenResponse>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await refreshTokenService.RotateForPlatformUserAsync(request.RefreshToken, cancellationToken);
        return tokens is null ? Unauthorized() : Ok(AuthTokenResponse.From(tokens));
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "ClientJwt")]
    [EnableRateLimiting("client-me")]
    public async Task<ActionResult<ClientProfileResponse>> Me(CancellationToken cancellationToken)
    {
        var profile = await clientProfileService.GetForPrincipalAsync(User, cancellationToken);
        return profile is null ? Forbid() : Ok(profile);
    }
}

public sealed class PlatformLoginRequest
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required, StringLength(256, MinimumLength = 20)]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc)
{
    public static AuthTokenResponse From(IssuedTokenPair tokens) => new(
        tokens.AccessToken,
        "Bearer",
        tokens.AccessTokenExpiresAtUtc,
        tokens.RefreshToken,
        tokens.RefreshTokenExpiresAtUtc);
}