using Microsoft.EntityFrameworkCore;
using fel_api.Domain.Agents;
using fel_api.Domain.Audit;
using fel_api.Domain.Catalog;
using fel_api.Domain.Documents;
using fel_api.Domain.Identity;
using fel_api.Domain.Subscriptions;
using fel_api.Domain.Tenants;

namespace fel_api.Data;

public sealed class FelApiDbContext(DbContextOptions<FelApiDbContext> options) : DbContext(options)
{
	public DbSet<Agent> Agents => Set<Agent>();
	public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
	public DbSet<PendingAgentCommand> PendingAgentCommands => Set<PendingAgentCommand>();
	public DbSet<Tenant> Tenants => Set<Tenant>();
	public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
	public DbSet<ExternalUserIdentity> ExternalUserIdentities => Set<ExternalUserIdentity>();
	public DbSet<PlatformAdministrator> PlatformAdministrators => Set<PlatformAdministrator>();
	public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
	public DbSet<TenantRole> TenantRoles => Set<TenantRole>();
	public DbSet<TenantUserRole> TenantUserRoles => Set<TenantUserRole>();
	public DbSet<TenantRoleCompany> TenantRoleCompanies => Set<TenantRoleCompany>();
	public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
	public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
	public DbSet<Company> Companies => Set<Company>();
	public DbSet<Branch> Branches => Set<Branch>();
	public DbSet<Certifier> Certifiers => Set<Certifier>();
	public DbSet<CertifierCountryConfiguration> CertifierCountryConfigurations => Set<CertifierCountryConfiguration>();
	public DbSet<CompanyCertifierAssignment> CompanyCertifierAssignments => Set<CompanyCertifierAssignment>();
	public DbSet<ActivationCode> ActivationCodes => Set<ActivationCode>();
	public DbSet<Document> Documents => Set<Document>();
	public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<Agent>(entity =>
		{
			entity.ToTable("Agents", table => table.HasCheckConstraint(
				"CK_Agents_CredentialHashLength",
				"LEN([CredentialHash]) = 64"));
			entity.HasKey(x => x.Id);
			entity.HasAlternateKey(x => new { x.TenantId, x.Id });
			entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
			entity.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
			entity.Property(x => x.CredentialHash).HasMaxLength(64).IsRequired();
			entity.Property(x => x.MachineHostname).HasMaxLength(255);
			entity.Property(x => x.MachineId).HasMaxLength(255);
			entity.Property(x => x.MachineOs).HasMaxLength(100);
			entity.Property(x => x.WorkerVersion).HasMaxLength(80);
			entity.HasIndex(x => new { x.TenantId, x.CompanyId, x.NormalizedName }).IsUnique();
			entity.HasOne(x => x.Company)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.CompanyId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<RefreshToken>(entity =>
		{
			entity.ToTable("RefreshTokens", table => table.HasCheckConstraint(
				"CK_RefreshTokens_ExactlyOneOwner",
				"([AgentId] IS NOT NULL AND [UserAccountId] IS NULL) OR ([AgentId] IS NULL AND [UserAccountId] IS NOT NULL)"));
			entity.HasKey(x => x.Id);
			entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
			entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
			entity.HasIndex(x => x.TokenHash).IsUnique();
			entity.HasIndex(x => new { x.FamilyId, x.RevokedAtUtc });
			entity.HasOne(x => x.Agent)
				.WithMany()
				.HasForeignKey(x => x.AgentId)
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.UserAccount)
				.WithMany()
				.HasForeignKey(x => x.UserAccountId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<PendingAgentCommand>(entity =>
		{
			entity.ToTable("PendingAgentCommands", table => table.HasCheckConstraint(
				"CK_PendingAgentCommands_LeaseAfterCreation",
				"[LeaseExpiresAtUtc] IS NULL OR [LeaseExpiresAtUtc] > [CreatedAtUtc]"));
			entity.HasKey(x => x.Id);
			entity.Property(x => x.CommandType).HasMaxLength(100).IsRequired();
			entity.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
			entity.HasIndex(x => new { x.AgentId, x.AcknowledgedAtUtc, x.LeaseExpiresAtUtc });
			entity.HasOne(x => x.Agent)
				.WithMany()
				.HasForeignKey(x => x.AgentId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Tenant>(entity =>
		{
			entity.ToTable("Tenants");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
		});

		modelBuilder.Entity<UserAccount>(entity =>
		{
			entity.ToTable("UserAccounts");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
			entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
			entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
			entity.Property(x => x.PasswordHash).HasMaxLength(1000);
			entity.HasIndex(x => x.NormalizedEmail).IsUnique();
		});

		modelBuilder.Entity<ExternalUserIdentity>(entity =>
		{
			entity.ToTable("ExternalUserIdentities");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
			entity.Property(x => x.Subject).HasMaxLength(255).IsRequired();
			entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
			entity.HasOne(x => x.UserAccount)
				.WithMany()
				.HasForeignKey(x => x.UserAccountId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<PlatformAdministrator>(entity =>
		{
			entity.ToTable("PlatformAdministrators");
			entity.HasKey(x => x.UserAccountId);
			entity.HasOne(x => x.UserAccount)
				.WithOne()
				.HasForeignKey<PlatformAdministrator>(x => x.UserAccountId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TenantMembership>(entity =>
		{
			entity.ToTable("TenantMemberships");
			entity.HasKey(x => new { x.TenantId, x.UserAccountId });
			entity.HasOne(x => x.Tenant)
				.WithMany()
				.HasForeignKey(x => x.TenantId)
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.UserAccount)
				.WithMany()
				.HasForeignKey(x => x.UserAccountId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TenantRole>(entity =>
		{
			entity.ToTable("TenantRoles");
			entity.HasKey(x => x.Id);
			entity.HasAlternateKey(x => new { x.TenantId, x.Id });
			entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
			entity.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
			entity.Property(x => x.Description).HasMaxLength(500);
			entity.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
			entity.HasOne(x => x.Tenant)
				.WithMany()
				.HasForeignKey(x => x.TenantId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TenantUserRole>(entity =>
		{
			entity.ToTable("TenantUserRoles");
			entity.HasKey(x => new { x.TenantId, x.UserAccountId, x.TenantRoleId });
			entity.HasOne(x => x.Membership)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.UserAccountId })
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.TenantRole)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.TenantRoleId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TenantRoleCompany>(entity =>
		{
			entity.ToTable("TenantRoleCompanies");
			entity.HasKey(x => new { x.TenantId, x.TenantRoleId, x.CompanyId });
			entity.HasOne(x => x.TenantRole)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.TenantRoleId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.Company)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.CompanyId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<SubscriptionPlan>(entity =>
		{
			entity.ToTable("SubscriptionPlans", table => table.HasCheckConstraint(
				"CK_SubscriptionPlans_Limits",
				"[MaxCompanies] > 0 AND [MaxBranches] > 0 AND [MaxUsers] > 0 AND [MaxAgents] > 0 AND [GracePeriodDays] >= 0"));
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
			entity.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
			entity.Property(x => x.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
			entity.Property(x => x.AllowedCountries).HasColumnType("nvarchar(max)");
			entity.Property(x => x.MaxAgents).HasDefaultValue(1);
			entity.HasIndex(x => x.NormalizedName).IsUnique();
		});

		modelBuilder.Entity<TenantSubscription>(entity =>
		{
			entity.ToTable("TenantSubscriptions", table => table.HasCheckConstraint(
				"CK_TenantSubscriptions_ExpirationAfterActivation",
				"[ExpiresAtUtc] > [ActivatedAtUtc]"));
			entity.HasKey(x => x.Id);
			entity.HasOne(x => x.Tenant)
				.WithMany()
				.HasForeignKey(x => x.TenantId)
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.SubscriptionPlan)
				.WithMany()
				.HasForeignKey(x => x.SubscriptionPlanId)
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasIndex(x => x.TenantId)
				.IsUnique()
				.HasFilter("[IsCurrent] = 1");
		});

		modelBuilder.Entity<Company>(entity =>
		{
			entity.ToTable("Companies", table => table.HasCheckConstraint(
				"CK_Companies_CountryCode",
				"[CountryCode] LIKE '[A-Z][A-Z]'"));
			entity.HasKey(x => x.Id);
			entity.HasAlternateKey(x => new { x.TenantId, x.Id });
			entity.HasAlternateKey(x => new { x.Id, x.CountryCode });
			entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
			entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
			entity.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
			entity.Property(x => x.TaxId).HasMaxLength(50);
			entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength();
			entity.Property(x => x.Timezone).HasMaxLength(100);
			entity.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
			entity.HasOne(x => x.Tenant)
				.WithMany()
				.HasForeignKey(x => x.TenantId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Branch>(entity =>
		{
			entity.ToTable("Branches");
			entity.HasKey(x => x.Id);
			entity.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
			entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
			entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
			entity.HasIndex(x => new { x.CompanyId, x.NormalizedName }).IsUnique();
			entity.HasOne(x => x.Company)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.CompanyId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Certifier>(entity =>
		{
			entity.ToTable("Certifiers");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
			entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
			entity.HasIndex(x => x.Code).IsUnique();
		});

		modelBuilder.Entity<CertifierCountryConfiguration>(entity =>
		{
			entity.ToTable("CertifierCountryConfigurations", table => table.HasCheckConstraint(
				"CK_CertifierCountryConfigurations_CountryCode",
				"[CountryCode] LIKE '[A-Z][A-Z]'"));
			entity.HasKey(x => x.Id);
			entity.HasAlternateKey(x => new { x.Id, x.CountryCode });
			entity.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
			entity.Property(x => x.EndpointUrl).HasMaxLength(2048).IsRequired();
			entity.Property(x => x.RequestFormat).HasConversion<string>().HasMaxLength(8).IsRequired();
			entity.HasIndex(x => new { x.CertifierId, x.CountryCode }).IsUnique();
			entity.HasOne(x => x.Certifier)
				.WithMany()
				.HasForeignKey(x => x.CertifierId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<CompanyCertifierAssignment>(entity =>
		{
			entity.ToTable("CompanyCertifierAssignments");
			entity.HasKey(x => x.CompanyId);
			entity.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
			entity.Property(x => x.CredentialsEncrypted).HasColumnType("varbinary(max)");
			entity.Property(x => x.UseProduction).HasDefaultValue(false);
			entity.HasOne(x => x.Company)
				.WithOne()
				.HasForeignKey<CompanyCertifierAssignment>(x => new { x.CompanyId, x.CountryCode })
				.HasPrincipalKey<Company>(x => new { x.Id, x.CountryCode })
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.CertifierCountryConfiguration)
				.WithMany()
				.HasForeignKey(x => new { x.CertifierCountryConfigurationId, x.CountryCode })
				.HasPrincipalKey(x => new { x.Id, x.CountryCode })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Document>(entity =>
		{
			entity.ToTable("Documents", table => table.HasCheckConstraint(
				"CK_Documents_AttemptCount",
				"[AttemptCount] >= 0"));
			entity.HasKey(x => x.Id);
			entity.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
			entity.Property(x => x.DocumentType).HasMaxLength(20).IsRequired();
			entity.Property(x => x.Series).HasMaxLength(50);
			entity.Property(x => x.Number).HasMaxLength(50);
			entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
			entity.Property(x => x.AuthorizationNumber).HasMaxLength(100);
			entity.Property(x => x.ErrorCode).HasMaxLength(100);
			entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
			entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
			entity.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status, x.IssuedAtUtc });
			entity.HasOne(x => x.Company)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.CompanyId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne<Agent>()
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.AgentId })
				.HasPrincipalKey(x => new { x.TenantId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
			entity.HasOne(x => x.Branch)
				.WithMany()
				.HasForeignKey(x => new { x.TenantId, x.CompanyId, x.BranchId })
				.HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id })
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<ActivationCode>(entity =>
		{
			entity.ToTable("ActivationCodes");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
			entity.Property(x => x.UsedByAgentId);
			entity.HasIndex(x => x.CodeHash).IsUnique();
		});

		modelBuilder.Entity<AuditLog>(entity =>
		{
			entity.ToTable("AuditLog");
			entity.HasKey(x => x.Id);
			entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
			entity.Property(x => x.ResourceType).HasMaxLength(100);
			entity.Property(x => x.ResourceId).HasMaxLength(200);
			entity.Property(x => x.IpAddress).HasMaxLength(45);
			entity.Property(x => x.Details).HasColumnType("nvarchar(max)");
			entity.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("IX_AuditLog_TenantId_OccurredAtUtc");
			entity.HasIndex(x => new { x.ResourceType, x.ResourceId }).HasDatabaseName("IX_AuditLog_ResourceType_ResourceId");
		});
	}
}