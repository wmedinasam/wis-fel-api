using fel_api.Data;
using fel_api.Domain.Catalog;
using fel_api.Domain.Tenants;
using fel_api.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace fel_api.Tests;

public sealed class CertifierConfigurationResolverTests
{
    [Fact]
    public async Task ResolveForCompanyAsync_ReturnsAssignedCountryConfiguration()
    {
        await using var dbContext = new FelApiDbContext(
            new DbContextOptionsBuilder<FelApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var tenant = new Tenant { Name = "Tenant" };
        var company = new Company
        {
            TenantId = tenant.Id,
            Name = "Company",
            NormalizedName = "COMPANY",
            CountryCode = "GT"
        };
        var certifier = new Certifier { Code = "INFILE", Name = "Infile" };
        var configuration = new CertifierCountryConfiguration
        {
            CertifierId = certifier.Id,
            CountryCode = "GT",
            EndpointUrl = "https://certifier.example/gt",
            RequestFormat = CertifierRequestFormat.Json
        };
        dbContext.Tenants.Add(tenant);
        dbContext.Companies.Add(company);
        dbContext.Certifiers.Add(certifier);
        dbContext.CertifierCountryConfigurations.Add(configuration);
        dbContext.CompanyCertifierAssignments.Add(new CompanyCertifierAssignment
        {
            CompanyId = company.Id,
            CountryCode = "GT",
            CertifierCountryConfigurationId = configuration.Id
        });
        await dbContext.SaveChangesAsync();

        var resolver = new CertifierConfigurationResolver(dbContext);

        var result = await resolver.ResolveForCompanyAsync(company.Id);

        Assert.Equal("INFILE", result.CertifierCode);
        Assert.Equal("GT", result.CountryCode);
        Assert.Equal("https://certifier.example/gt", result.EndpointUrl);
        Assert.Equal(CertifierRequestFormat.Json, result.RequestFormat);
    }
}