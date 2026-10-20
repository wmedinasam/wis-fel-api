using System.Text.Json;
using fel_api.Auth;
using fel_api.Domain.Agents;
using fel_api.Domain.Audit;
using fel_api.Domain.Catalog;
using fel_api.Domain.Documents;
using fel_api.Domain.Identity;
using fel_api.Domain.Subscriptions;
using fel_api.Domain.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace fel_api.Data;

// Development only. Fills every table with sample data; a no-op once any tenant exists.
public static class DevelopmentDataSeeder
{
    public const string AdminEmail = "admin@fel.local";
    public const string AdminPassword = "Correct-Horse-2026!";
    public const string AgentSecret = "dev-agent-secret-0123456789abcdef";
    public const string OwnerSubject = "seed-owner";
    public const string RestrictedSubject = "seed-restricted";

    public static async Task SeedAsync(
        FelApiDbContext db,
        IPasswordHasher<UserAccount> passwordHasher,
        string? clientIssuer,
        CancellationToken cancellationToken = default)
    {
        if (await db.Tenants.AnyAsync(cancellationToken))
        {
            return;
        }

        var issuer = string.IsNullOrWhiteSpace(clientIssuer) ? "https://idp.dev.local" : clientIssuer;
        var now = DateTimeOffset.UtcNow;
        var secretHash = TokenHashing.Hash(AgentSecret);

        var tenant = new Tenant { Name = "Cliente Demo" };
        var plan = new SubscriptionPlan
        {
            Name = "Pro",
            NormalizedName = "PRO",
            BillingCycle = PlanBillingCycle.Annual,
            MaxCompanies = 5,
            MaxBranches = 5,
            MaxUsers = 10,
            MaxAgents = 5,
            GracePeriodDays = 3,
            AllowedCountries = ["GT", "SV"]
        };
        db.Tenants.Add(tenant);
        db.SubscriptionPlans.Add(plan);
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            SubscriptionPlanId = plan.Id,
            ActivatedAtUtc = now.AddDays(-30),
            ExpiresAtUtc = now.AddYears(1)
        });

        var admin = CreateUser(AdminEmail, "Administrador demo");
        admin.PasswordHash = passwordHasher.HashPassword(admin, AdminPassword);
        db.UserAccounts.Add(admin);
        db.PlatformAdministrators.Add(new PlatformAdministrator { UserAccountId = admin.Id });

        var owner = CreateUser("owner@cliente.test", "Dueño demo");
        var restricted = CreateUser("restricted@cliente.test", "Usuario SV demo");
        db.UserAccounts.AddRange(owner, restricted);
        db.ExternalUserIdentities.AddRange(
            new ExternalUserIdentity { UserAccountId = owner.Id, Issuer = issuer, Subject = OwnerSubject },
            new ExternalUserIdentity { UserAccountId = restricted.Id, Issuer = issuer, Subject = RestrictedSubject });
        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = tenant.Id, UserAccountId = owner.Id },
            new TenantMembership { TenantId = tenant.Id, UserAccountId = restricted.Id });

        // The Owner role has no TenantRoleCompanies rows, so it reaches every company.
        var ownerRole = new TenantRole { TenantId = tenant.Id, Name = "Owner", NormalizedName = "OWNER", Description = "Todas las empresas" };
        var userRole = new TenantRole { TenantId = tenant.Id, Name = "Usuario", NormalizedName = "USUARIO", Description = "Solo Empresa SV" };
        db.TenantRoles.AddRange(ownerRole, userRole);
        db.TenantUserRoles.AddRange(
            new TenantUserRole { TenantId = tenant.Id, UserAccountId = owner.Id, TenantRoleId = ownerRole.Id },
            new TenantUserRole { TenantId = tenant.Id, UserAccountId = restricted.Id, TenantRoleId = userRole.Id });

        var gt = new Company { TenantId = tenant.Id, Name = "Empresa GT", NormalizedName = "EMPRESA GT", CountryCode = "GT", TaxId = "1234567-8", CurrencyCode = "GTQ", Timezone = "America/Guatemala" };
        var sv = new Company { TenantId = tenant.Id, Name = "Empresa SV", NormalizedName = "EMPRESA SV", CountryCode = "SV", TaxId = "0614-010101-001-1", CurrencyCode = "USD", Timezone = "America/El_Salvador" };
        db.Companies.AddRange(gt, sv);
        db.TenantRoleCompanies.Add(new TenantRoleCompany { TenantId = tenant.Id, TenantRoleId = userRole.Id, CompanyId = sv.Id });

        var gtBranch = new Branch { TenantId = tenant.Id, CompanyId = gt.Id, Name = "Casa Central", NormalizedName = "CASA CENTRAL" };
        var svBranch = new Branch { TenantId = tenant.Id, CompanyId = sv.Id, Name = "Sucursal San Salvador", NormalizedName = "SUCURSAL SAN SALVADOR" };
        db.Branches.AddRange(gtBranch, svBranch);

        var infile = new Certifier { Code = "INFILE", Name = "INFILE" };
        var infileGt = new CertifierCountryConfiguration { CertifierId = infile.Id, CountryCode = "GT", EndpointUrl = "https://certificador.gt.test/api", RequestFormat = CertifierRequestFormat.Xml };
        var infileSv = new CertifierCountryConfiguration { CertifierId = infile.Id, CountryCode = "SV", EndpointUrl = "https://certificador.sv.test/api", RequestFormat = CertifierRequestFormat.Json };
        db.Certifiers.Add(infile);
        db.CertifierCountryConfigurations.AddRange(infileGt, infileSv);
        db.CompanyCertifierAssignments.AddRange(
            new CompanyCertifierAssignment { CompanyId = gt.Id, CountryCode = "GT", CertifierCountryConfigurationId = infileGt.Id },
            new CompanyCertifierAssignment { CompanyId = sv.Id, CountryCode = "SV", CertifierCountryConfigurationId = infileSv.Id });

        var gtAgent = new Agent { TenantId = tenant.Id, CompanyId = gt.Id, Name = "Worker GT", NormalizedName = "WORKER GT", CredentialHash = secretHash, MachineHostname = "oficina-gt", MachineOs = "Windows 11", WorkerVersion = "1.0.0", LastHeartbeatAtUtc = now };
        var svAgent = new Agent { TenantId = tenant.Id, CompanyId = sv.Id, Name = "Worker SV", NormalizedName = "WORKER SV", CredentialHash = secretHash, MachineHostname = "oficina-sv", MachineOs = "Ubuntu 24.04", WorkerVersion = "1.0.0" };
        db.Agents.AddRange(gtAgent, svAgent);

        db.ActivationCodes.Add(new ActivationCode
        {
            TenantId = tenant.Id,
            CompanyId = sv.Id,
            CodeHash = TokenHashing.Hash("DEV-ACTIVATION-SV"),
            ExpiresAtUtc = now.AddDays(7)
        });
        db.RefreshTokens.Add(new RefreshToken
        {
            AgentId = gtAgent.Id,
            TokenHash = TokenHashing.Hash("dev-refresh-gt"),
            ExpiresAtUtc = now.AddDays(30)
        });

        var errorDocument = NewDocument(gtAgent, gtBranch.Id, "GT-0001", DocumentStatus.Error, 1, now.AddHours(-3));
        errorDocument.ResendRequestedAtUtc = now.AddMinutes(-5);
        db.Documents.AddRange(
            errorDocument,
            NewDocument(gtAgent, gtBranch.Id, "GT-0002", DocumentStatus.Certified, 2, now.AddHours(-2)),
            NewDocument(gtAgent, gtBranch.Id, "GT-0003", DocumentStatus.Retrying, 3, now.AddHours(-1)),
            NewDocument(gtAgent, null, "GT-0004", DocumentStatus.Pending, 4, now.AddMinutes(-10)),
            NewDocument(svAgent, svBranch.Id, "SV-0001", DocumentStatus.Certified, 1, now.AddHours(-2)),
            NewDocument(svAgent, svBranch.Id, "SV-0002", DocumentStatus.Processing, 2, now.AddMinutes(-20)));
        db.PendingAgentCommands.Add(new PendingAgentCommand
        {
            AgentId = gtAgent.Id,
            CommandType = "ResendDocument",
            Payload = JsonSerializer.Serialize(new { documentId = errorDocument.Id, idempotencyKey = errorDocument.IdempotencyKey })
        });

        db.AuditLogs.AddRange(
            new AuditLog { TenantId = tenant.Id, AgentId = gtAgent.Id, EventType = "agent.provisioned", ResourceType = "Agent", ResourceId = gtAgent.Id.ToString() },
            new AuditLog { TenantId = tenant.Id, UserAccountId = admin.Id, EventType = "tenant.seeded", ResourceType = "Tenant", ResourceId = tenant.Id.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"Datos de prueba creados. Admin: {AdminEmail} / {AdminPassword}. Agente: {AgentSecret}");
    }

    private static UserAccount CreateUser(string email, string displayName) => new()
    {
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = displayName
    };

    private static Document NewDocument(Agent agent, Guid? branchId, string key, DocumentStatus status, int number, DateTimeOffset issuedAt)
    {
        var certified = status == DocumentStatus.Certified;
        var error = status == DocumentStatus.Error;
        return new Document
        {
            TenantId = agent.TenantId,
            CompanyId = agent.CompanyId,
            BranchId = branchId,
            AgentId = agent.Id,
            IdempotencyKey = key,
            DocumentType = "FACT",
            Series = "A",
            Number = number.ToString(),
            Status = status,
            AttemptCount = certified ? 1 : 2,
            IssuedAtUtc = issuedAt,
            CertifiedAtUtc = certified ? issuedAt.AddSeconds(5) : null,
            AuthorizationNumber = certified ? Guid.NewGuid().ToString("D") : null,
            ErrorCode = error ? "CERT-400" : null,
            ErrorMessage = error ? "Rechazado por el certificador (dato de prueba)." : null
        };
    }
}
