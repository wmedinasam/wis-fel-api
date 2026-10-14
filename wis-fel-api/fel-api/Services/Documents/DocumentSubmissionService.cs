using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using fel_api.Data;
using fel_api.Domain.Catalog;
using fel_api.Domain.Documents;
using fel_api.Services.Agents;
using fel_api.Services.Catalog;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Services.Documents;

public interface ICertifierRequestMapper
{
    bool CanMap(string certifierCode, string countryCode, CertifierRequestFormat requestFormat);

    string Map(JsonElement workerPayload, ResolvedCertifierConfiguration configuration);
}

public sealed record DocumentSubmissionResult(Guid DocumentId, int HttpStatusCode, bool Delivered);

public sealed class DocumentSubmissionService(
    FelApiDbContext dbContext,
    AgentLicenseValidator licenseValidator,
    CertifierConfigurationResolver certifierResolver,
    IEnumerable<ICertifierRequestMapper> mappers,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider)
{
    public async Task<DocumentSubmissionResult> SubmitAsync(
        Guid agentId,
        string? idempotencyKey,
        Guid? branchId,
        JsonElement workerPayload,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == agentId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found or is inactive.");
        await licenseValidator.ValidateAsync(agent.TenantId, cancellationToken);

        if (branchId is not null && !await dbContext.Branches.AnyAsync(
            x => x.Id == branchId && x.TenantId == agent.TenantId && x.CompanyId == agent.CompanyId,
            cancellationToken))
        {
            throw new AgentBranchNotFoundException();
        }

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKey.Trim();
        if (normalizedIdempotencyKey.Length > 200)
        {
            throw new ArgumentException("Idempotency-Key must be 200 characters or fewer.", nameof(idempotencyKey));
        }

        var duplicate = await dbContext.DocumentSubmissions.AnyAsync(
            x => x.TenantId == agent.TenantId && x.IdempotencyKey == normalizedIdempotencyKey,
            cancellationToken);
        if (duplicate)
        {
            throw new DuplicateDocumentSubmissionException();
        }

        var configuration = await certifierResolver.ResolveForCompanyAsync(
            agent.TenantId,
            agent.CompanyId,
            cancellationToken);
        var document = new DocumentSubmission
        {
            TenantId = agent.TenantId,
            CompanyId = agent.CompanyId,
            AgentId = agent.Id,
            BranchId = branchId,
            IdempotencyKey = normalizedIdempotencyKey,
            IncomingPayload = workerPayload.GetRawText(),
            ReceivedAtUtc = timeProvider.GetUtcNow()
        };
        dbContext.DocumentSubmissions.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        var mapper = mappers.SingleOrDefault(x => x.CanMap(
            configuration.CertifierCode,
            configuration.CountryCode,
            configuration.RequestFormat));
        if (mapper is null)
        {
            throw new CertifierRequestMapperNotConfiguredException(document.Id);
        }

        var outboundRequest = mapper.Map(workerPayload, configuration);
        var attempt = new DocumentDeliveryAttempt
        {
            DocumentSubmissionId = document.Id,
            AttemptNumber = 1,
            RequestPayload = outboundRequest,
            StartedAtUtc = timeProvider.GetUtcNow()
        };
        dbContext.DocumentDeliveryAttempts.Add(attempt);

        try
        {
            var client = httpClientFactory.CreateClient("Certifier");
            using var request = new HttpRequestMessage(HttpMethod.Post, ValidateEndpoint(configuration.EndpointUrl))
            {
                Content = new StringContent(outboundRequest, Encoding.UTF8,
                    configuration.RequestFormat == CertifierRequestFormat.Json ? "application/json" : "application/xml")
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            attempt.HttpStatusCode = (int)response.StatusCode;
            attempt.ResponsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
            attempt.Succeeded = response.IsSuccessStatusCode;
            attempt.CompletedAtUtc = timeProvider.GetUtcNow();
            if (!response.IsSuccessStatusCode)
            {
                attempt.ErrorDetails = $"Certifier returned HTTP {(int)response.StatusCode}.";
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new DocumentSubmissionResult(
                document.Id,
                response.IsSuccessStatusCode ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway,
                response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            attempt.Succeeded = false;
            attempt.ErrorDetails = "Certifier request timed out.";
            attempt.CompletedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DocumentSubmissionResult(document.Id, StatusCodes.Status504GatewayTimeout, false);
        }
        catch (HttpRequestException)
        {
            attempt.Succeeded = false;
            attempt.ErrorDetails = "Certifier request failed before receiving a response.";
            attempt.CompletedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DocumentSubmissionResult(document.Id, StatusCodes.Status502BadGateway, false);
        }
    }

    private static Uri ValidateEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new InvalidOperationException("Certifier endpoint must be an absolute HTTPS URL.");
        }

        return endpoint;
    }
}

public sealed class AgentBranchNotFoundException()
    : InvalidOperationException("The requested branch is not part of the authenticated Agent's company.");

public sealed class DuplicateDocumentSubmissionException()
    : InvalidOperationException("The Idempotency-Key has already been used by this tenant.");

public sealed class CertifierRequestMapperNotConfiguredException(Guid documentId)
    : InvalidOperationException("No request mapper is registered for the assigned certifier, country, and format.")
{
    public Guid DocumentId { get; } = documentId;
}