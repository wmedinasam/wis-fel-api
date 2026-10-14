using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace fel_api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentAuthAndDocumentRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentDeliveryAttempts_DocumentSubmissions_DocumentSubmissionId",
                table: "DocumentDeliveryAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSubmissions_Branches_TenantId_CompanyId_BranchId",
                table: "DocumentSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentSubmissions_Companies_TenantId_CompanyId",
                table: "DocumentSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPlans_Limits",
                table: "SubscriptionPlans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentSubmissions",
                table: "DocumentSubmissions");

            migrationBuilder.RenameTable(
                name: "DocumentSubmissions",
                newName: "Documents");

            migrationBuilder.RenameIndex(
                name: "IX_DocumentSubmissions_TenantId_IdempotencyKey",
                table: "Documents",
                newName: "IX_Documents_TenantId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_DocumentSubmissions_TenantId_CompanyId_BranchId",
                table: "Documents",
                newName: "IX_Documents_TenantId_CompanyId_BranchId");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "UserAccounts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAgents",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Documents",
                table: "Documents",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CredentialHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastHandshakeAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WorkerVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.UniqueConstraint("AK_Agents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Agents_CredentialHashLength", "LEN([CredentialHash]) = 64");
                    table.ForeignKey(
                        name: "FK_Agents_Companies_TenantId_CompanyId",
                        columns: x => new { x.TenantId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalUserIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalUserIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalUserIdentities_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PendingAgentCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingAgentCommands", x => x.Id);
                    table.CheckConstraint("CK_PendingAgentCommands_LeaseAfterCreation", "[LeaseExpiresAtUtc] IS NULL OR [LeaseExpiresAtUtc] > [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_PendingAgentCommands_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.CheckConstraint("CK_RefreshTokens_ExactlyOneOwner", "([AgentId] IS NOT NULL AND [UserAccountId] IS NULL) OR ([AgentId] IS NULL AND [UserAccountId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPlans_Limits",
                table: "SubscriptionPlans",
                sql: "[MaxCompanies] > 0 AND [MaxBranches] > 0 AND [MaxUsers] > 0 AND [MaxAgents] > 0 AND [GracePeriodDays] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_TenantId_CompanyId_NormalizedName",
                table: "Agents",
                columns: new[] { "TenantId", "CompanyId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUserIdentities_Issuer_Subject",
                table: "ExternalUserIdentities",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUserIdentities_UserAccountId",
                table: "ExternalUserIdentities",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAgentCommands_AgentId_AcknowledgedAtUtc_LeaseExpiresAtUtc",
                table: "PendingAgentCommands",
                columns: new[] { "AgentId", "AcknowledgedAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AgentId",
                table: "RefreshTokens",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId_RevokedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "FamilyId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserAccountId",
                table: "RefreshTokens",
                column: "UserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentDeliveryAttempts_Documents_DocumentSubmissionId",
                table: "DocumentDeliveryAttempts",
                column: "DocumentSubmissionId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" },
                principalTable: "Agents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Branches_TenantId_CompanyId_BranchId",
                table: "Documents",
                columns: new[] { "TenantId", "CompanyId", "BranchId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Companies_TenantId_CompanyId",
                table: "Documents",
                columns: new[] { "TenantId", "CompanyId" },
                principalTable: "Companies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentDeliveryAttempts_Documents_DocumentSubmissionId",
                table: "DocumentDeliveryAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Branches_TenantId_CompanyId_BranchId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Companies_TenantId_CompanyId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "ExternalUserIdentities");

            migrationBuilder.DropTable(
                name: "PendingAgentCommands");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionPlans_Limits",
                table: "SubscriptionPlans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Documents",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MaxAgents",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Documents");

            migrationBuilder.RenameTable(
                name: "Documents",
                newName: "DocumentSubmissions");

            migrationBuilder.RenameIndex(
                name: "IX_Documents_TenantId_IdempotencyKey",
                table: "DocumentSubmissions",
                newName: "IX_DocumentSubmissions_TenantId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_Documents_TenantId_CompanyId_BranchId",
                table: "DocumentSubmissions",
                newName: "IX_DocumentSubmissions_TenantId_CompanyId_BranchId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentSubmissions",
                table: "DocumentSubmissions",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionPlans_Limits",
                table: "SubscriptionPlans",
                sql: "[MaxCompanies] > 0 AND [MaxBranches] > 0 AND [MaxUsers] > 0 AND [GracePeriodDays] >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentDeliveryAttempts_DocumentSubmissions_DocumentSubmissionId",
                table: "DocumentDeliveryAttempts",
                column: "DocumentSubmissionId",
                principalTable: "DocumentSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSubmissions_Branches_TenantId_CompanyId_BranchId",
                table: "DocumentSubmissions",
                columns: new[] { "TenantId", "CompanyId", "BranchId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentSubmissions_Companies_TenantId_CompanyId",
                table: "DocumentSubmissions",
                columns: new[] { "TenantId", "CompanyId" },
                principalTable: "Companies",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
