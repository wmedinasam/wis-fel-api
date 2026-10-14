using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Agents;
using fel_api.Domain.Subscriptions;
using fel_api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Agents;

public sealed record AgentHandshakeResult(
    IssuedTokenPair Tokens,
    TenantSubscriptionState SubscriptionState,
    DateTimeOffset HandshakenAtUtc,
    Guid TenantId,
    Guid CompanyId);

public sealed class AgentHandshakeService(
    FelApiDbContext dbContext,
    AgentLicenseValidator licenseValidator,
    RefreshTokenService refreshTokenService,
    TimeProvider timeProvider)
{
    public async Task<AgentHandshakeResult?> HandshakeAsync(
        Guid agentId,
        string secret,
        string workerVersion,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == agentId && x.IsActive, cancellationToken);

        if (agent is null || !TokenHashing.Matches(secret, agent.CredentialHash))
        {
            return null;
        }

        var subscriptionState = await licenseValidator.ValidateAsync(agent.TenantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        agent.LastHandshakeAtUtc = now;
        agent.WorkerVersion = workerVersion.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        var tokens = await refreshTokenService.IssueForAgentAsync(agent, cancellationToken);
        return new AgentHandshakeResult(tokens, subscriptionState, now, agent.TenantId, agent.CompanyId);
    }
}