using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Agents;
using fel_api.Domain.Identity;
using fel_api.Domain.Subscriptions;
using fel_api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace fel_api.Services.Auth;

public sealed class RefreshTokenService(
    FelApiDbContext dbContext,
    JwtTokenIssuer tokenIssuer,
    AgentLicenseValidator agentLicenseValidator,
    TimeProvider timeProvider,
    IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public Task<IssuedTokenPair> IssueForPlatformUserAsync(
        UserAccount userAccount,
        CancellationToken cancellationToken = default) =>
        IssueAsync(userAccount, null, cancellationToken);

    public Task<IssuedTokenPair> IssueForAgentAsync(
        Agent agent,
        CancellationToken cancellationToken = default) =>
        IssueAsync(null, agent, cancellationToken);

    public async Task<IssuedTokenPair?> RotateForPlatformUserAsync(
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        (await RotateAsync(refreshToken, isPlatformUser: true, cancellationToken))?.Tokens;

    public Task<AgentRotation?> RotateForAgentAsync(
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        RotateAsync(refreshToken, isPlatformUser: false, cancellationToken);

    private async Task<IssuedTokenPair> IssueAsync(
        UserAccount? userAccount,
        Agent? agent,
        CancellationToken cancellationToken)
    {
        var rawToken = TokenHashing.CreateSecret();
        var now = timeProvider.GetUtcNow();
        var expiresAt = userAccount is not null
            ? now.AddDays(_options.PlatformRefreshTokenDays)
            : now.AddDays(_options.AgentRefreshTokenDays);
        var record = new RefreshToken
        {
            AgentId = agent?.Id,
            UserAccountId = userAccount?.Id,
            TokenHash = TokenHashing.Hash(rawToken),
            ExpiresAtUtc = expiresAt
        };

        dbContext.RefreshTokens.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = userAccount is not null
            ? tokenIssuer.IssuePlatformToken(userAccount)
            : tokenIssuer.IssueAgentToken(agent!);

        return new IssuedTokenPair(accessToken.Value, accessToken.ExpiresAtUtc, rawToken, expiresAt);
    }

    private async Task<AgentRotation?> RotateAsync(
        string rawToken,
        bool isPlatformUser,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenHash = TokenHashing.Hash(rawToken);
        var tokenRecord = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (tokenRecord is null)
        {
            return null;
        }

        if (tokenRecord.RevokedAtUtc is not null)
        {
            if (tokenRecord.ReplacedByTokenHash is not null)
            {
                await RevokeFamilyAsync(tokenRecord.FamilyId, now, cancellationToken);
            }

            return null;
        }

        if (tokenRecord.ExpiresAtUtc <= now || (isPlatformUser != (tokenRecord.UserAccountId is not null)))
        {
            tokenRecord.RevokedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        UserAccount? userAccount = null;
        Agent? agent = null;
        TenantSubscriptionState? licenseState = null;
        if (isPlatformUser)
        {
            userAccount = await dbContext.UserAccounts
                .SingleOrDefaultAsync(x => x.Id == tokenRecord.UserAccountId && x.IsActive, cancellationToken);
            var isAdministrator = userAccount is not null && await dbContext.PlatformAdministrators
                .AnyAsync(x => x.UserAccountId == userAccount.Id, cancellationToken);
            if (!isAdministrator)
            {
                tokenRecord.RevokedAtUtc = now;
                await dbContext.SaveChangesAsync(cancellationToken);
                return null;
            }
        }
        else
        {
            agent = await dbContext.Agents
                .SingleOrDefaultAsync(x => x.Id == tokenRecord.AgentId && x.IsActive, cancellationToken);
            if (agent is null)
            {
                tokenRecord.RevokedAtUtc = now;
                await dbContext.SaveChangesAsync(cancellationToken);
                return null;
            }

            licenseState = await agentLicenseValidator.ValidateAsync(agent.TenantId, cancellationToken);
        }

        var replacementRawToken = TokenHashing.CreateSecret();
        var replacementHash = TokenHashing.Hash(replacementRawToken);
        var replacementExpiresAt = isPlatformUser
            ? now.AddDays(_options.PlatformRefreshTokenDays)
            : now.AddDays(_options.AgentRefreshTokenDays);
        tokenRecord.RevokedAtUtc = now;
        tokenRecord.ReplacedByTokenHash = replacementHash;
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            FamilyId = tokenRecord.FamilyId,
            AgentId = tokenRecord.AgentId,
            UserAccountId = tokenRecord.UserAccountId,
            TokenHash = replacementHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = replacementExpiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = userAccount is not null
            ? tokenIssuer.IssuePlatformToken(userAccount)
            : tokenIssuer.IssueAgentToken(agent!);
        return new AgentRotation(
            new IssuedTokenPair(accessToken.Value, accessToken.ExpiresAtUtc, replacementRawToken, replacementExpiresAt),
            licenseState);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
    {
        var family = await dbContext.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.RevokedAtUtc = revokedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Tokens rotated for an agent, plus the license state the refresh just validated.</summary>
public sealed record AgentRotation(IssuedTokenPair Tokens, TenantSubscriptionState? LicenseState);
