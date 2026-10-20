using fel_api.Data;
using fel_api.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Catalog;

public sealed class CertifierCredentialService(
    FelApiDbContext dbContext,
    CertifierCredentialProtector credentialProtector,
    TimeProvider timeProvider)
{
    public async Task SetCredentialsAsync(
        Guid companyId,
        IReadOnlyDictionary<string, string> credentials,
        bool useProduction,
        Guid? userAccountId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var assignment = await dbContext.CompanyCertifierAssignments
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw new KeyNotFoundException($"Company '{companyId}' has no certifier assignment.");

        assignment.CredentialsEncrypted = credentialProtector.Protect(credentials);
        assignment.UseProduction = useProduction;
        dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = assignment.Company.TenantId,
            UserAccountId = userAccountId,
            EventType = "certifier-credentials-updated",
            ResourceType = "CompanyCertifierAssignment",
            ResourceId = companyId.ToString(),
            IpAddress = ipAddress,
            OccurredAtUtc = timeProvider.GetUtcNow()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
