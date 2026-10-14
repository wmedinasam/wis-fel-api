using fel_api.Data;
using fel_api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace fel_api.Services.Agents;

public sealed class AgentHeartbeatOptions
{
    public const string SectionName = "AgentHeartbeat";

    public int CommandLeaseSeconds { get; set; } = 30;
    public int MaxCommandsPerHeartbeat { get; set; } = 20;
}

public sealed record PendingAgentCommandResult(Guid Id, string Type, string Payload, DateTimeOffset LeaseExpiresAtUtc);

public sealed record AgentHeartbeatResult(DateTimeOffset ServerTimeUtc, IReadOnlyList<PendingAgentCommandResult> PendingCommands);

public sealed class AgentHeartbeatService(
    FelApiDbContext dbContext,
    AgentLicenseValidator licenseValidator,
    TimeProvider timeProvider,
    IOptions<AgentHeartbeatOptions> options)
{
    public async Task<AgentHeartbeatResult> HeartbeatAsync(
        Guid agentId,
        string workerVersion,
        IReadOnlyCollection<Guid> acknowledgedCommandIds,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == agentId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found or is inactive.");
        await licenseValidator.ValidateAsync(agent.TenantId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        agent.LastHeartbeatAtUtc = now;
        agent.WorkerVersion = workerVersion.Trim();

        var acknowledgments = await dbContext.PendingAgentCommands
            .Where(x => x.AgentId == agentId
                && acknowledgedCommandIds.Contains(x.Id)
                && x.AcknowledgedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var command in acknowledgments)
        {
            command.AcknowledgedAtUtc = now;
        }

        var commandLeaseSeconds = options.Value.CommandLeaseSeconds;
        var maxCommands = options.Value.MaxCommandsPerHeartbeat;
        var commands = await dbContext.PendingAgentCommands
            .Where(x => x.AgentId == agentId
                && x.AcknowledgedAtUtc == null
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(maxCommands)
            .ToListAsync(cancellationToken);

        var leaseExpiresAtUtc = now.AddSeconds(commandLeaseSeconds);
        foreach (var command in commands)
        {
            command.LeaseExpiresAtUtc = leaseExpiresAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AgentHeartbeatResult(
            now,
            commands.Select(x => new PendingAgentCommandResult(
                x.Id,
                x.CommandType,
                x.Payload,
                leaseExpiresAtUtc)).ToArray());
    }
}