using fel_api.Auth;
using fel_api.Data;
using fel_api.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Agents;

public sealed record ProvisionedAgent(Guid AgentId, string Secret);

public sealed class AgentProvisioningService(
    FelApiDbContext dbContext,
    AgentLicenseValidator licenseValidator,
    TimeProvider timeProvider)
{
    public async Task<ProvisionedAgent> ProvisionAsync(
        Guid tenantId,
        Guid companyId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Agent name is required.", nameof(name));
        }

        await licenseValidator.ValidateAsync(tenantId, cancellationToken);
        var plan = await dbContext.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsCurrent)
            .Select(x => x.SubscriptionPlan)
            .SingleAsync(cancellationToken);
        var activeAgentCount = await dbContext.Agents
            .CountAsync(x => x.TenantId == tenantId && x.IsActive, cancellationToken);

        if (activeAgentCount >= plan.MaxAgents)
        {
            throw new AgentLimitExceededException(tenantId, plan.MaxAgents);
        }

        var secret = TokenHashing.CreateSecret();
        var normalizedName = name.Trim().ToUpperInvariant();
        var companyBelongsToTenant = await dbContext.Companies
            .AnyAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken);
        if (!companyBelongsToTenant)
        {
            throw new AgentCompanyNotFoundException();
        }

        var nameAlreadyExists = await dbContext.Agents
            .AnyAsync(x => x.TenantId == tenantId
                && x.CompanyId == companyId
                && x.NormalizedName == normalizedName,
                cancellationToken);
        if (nameAlreadyExists)
        {
            throw new AgentNameAlreadyExistsException();
        }

        var agent = new Agent
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Name = name.Trim(),
            NormalizedName = normalizedName,
            CredentialHash = TokenHashing.Hash(secret),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProvisionedAgent(agent.Id, secret);
    }

    public async Task RevokeAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found.");

        agent.IsActive = false;
        var now = timeProvider.GetUtcNow();
        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(x => x.AgentId == agentId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeRefreshTokens)
        {
            token.RevokedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AgentLimitExceededException(Guid tenantId, int limit)
    : InvalidOperationException($"Tenant '{tenantId}' has reached its agent limit of {limit}.");

public sealed class AgentCompanyNotFoundException()
    : InvalidOperationException("The company does not belong to the requested tenant.");

public sealed class AgentNameAlreadyExistsException()
    : InvalidOperationException("An Agent with this name already exists for the company.");