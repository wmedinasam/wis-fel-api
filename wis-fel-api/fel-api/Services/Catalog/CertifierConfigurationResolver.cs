using fel_api.Data;
using fel_api.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Catalog;

public sealed record ResolvedCertifierConfiguration(
    string CertifierCode,
    string CountryCode,
    string EndpointUrl,
    CertifierRequestFormat RequestFormat,
    bool UseProduction,
    IReadOnlyDictionary<string, string> Credentials);

public sealed class CertifierConfigurationResolver(
    FelApiDbContext dbContext,
    CertifierCredentialProtector credentialProtector)
{
    public async Task<ResolvedCertifierConfiguration> ResolveForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await ResolveForCompanyAsync(null, companyId, cancellationToken);

    public async Task<ResolvedCertifierConfiguration> ResolveForCompanyAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await ResolveForCompanyAsync((Guid?)tenantId, companyId, cancellationToken);

    /// <summary>Null when the agent is revoked or does not belong to the company: its token must not expose secrets.</summary>
    public async Task<ResolvedCertifierConfiguration?> ResolveForActiveAgentAsync(
        Guid agentId,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var agentIsActive = await dbContext.Agents
            .AsNoTracking()
            .AnyAsync(x => x.Id == agentId && x.IsActive && x.TenantId == tenantId && x.CompanyId == companyId, cancellationToken);

        return agentIsActive ? await ResolveForCompanyAsync(tenantId, companyId, cancellationToken) : null;
    }

    private async Task<ResolvedCertifierConfiguration> ResolveForCompanyAsync(
        Guid? tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.CompanyCertifierAssignments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && (tenantId == null || x.Company.TenantId == tenantId))
            .Select(x => new
            {
                x.CountryCode,
                x.UseProduction,
                x.CredentialsEncrypted,
                x.CertifierCountryConfiguration.EndpointUrl,
                x.CertifierCountryConfiguration.RequestFormat,
                x.CertifierCountryConfiguration.IsActive,
                x.CertifierCountryConfiguration.Certifier.Code,
                CertifierIsActive = x.CertifierCountryConfiguration.Certifier.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (configuration is null)
        {
            var companyExists = await dbContext.Companies
                .AsNoTracking()
                .AnyAsync(x => x.Id == companyId && (tenantId == null || x.TenantId == tenantId), cancellationToken);

            if (!companyExists)
            {
                throw new KeyNotFoundException($"Company '{companyId}' was not found.");
            }

            throw new InvalidOperationException($"Company '{companyId}' has no certifier configuration.");
        }

        if (!configuration.IsActive || !configuration.CertifierIsActive)
        {
            throw new InvalidOperationException($"Company '{companyId}' has an inactive certifier configuration.");
        }

        return new ResolvedCertifierConfiguration(
            configuration.Code,
            configuration.CountryCode,
            configuration.EndpointUrl,
            configuration.RequestFormat,
            configuration.UseProduction,
            configuration.CredentialsEncrypted is null
                ? new Dictionary<string, string>()
                : credentialProtector.Unprotect(configuration.CredentialsEncrypted));
    }
}
