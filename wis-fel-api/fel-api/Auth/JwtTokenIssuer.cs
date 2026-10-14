using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using fel_api.Domain.Agents;
using fel_api.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace fel_api.Auth;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _signingCredentials = new(
        new SymmetricSecurityKey(Convert.FromBase64String(options.Value.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public IssuedAccessToken IssuePlatformToken(UserAccount userAccount)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.PlatformTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userAccount.Id.ToString()),
            new Claim("principal_type", "platform_admin"),
            new Claim(JwtRegisteredClaimNames.Email, userAccount.Email)
        };

        return Issue(claims, _options.PlatformAudience, now, expiresAt);
    }

    public IssuedAccessToken IssueAgentToken(Agent agent)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AgentTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim("principal_type", "agent"),
            new Claim("agent_id", agent.Id.ToString()),
            new Claim("tenant_id", agent.TenantId.ToString()),
            new Claim("company_id", agent.CompanyId.ToString())
        };

        return Issue(claims, _options.AgentAudience, now, expiresAt);
    }

    private IssuedAccessToken Issue(
        IEnumerable<Claim> claims,
        string audience,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}