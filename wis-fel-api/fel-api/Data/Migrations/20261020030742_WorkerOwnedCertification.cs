using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace fel_api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkerOwnedCertification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentDeliveryAttempts");

            // Existing rows are development payloads with no status metadata; they cannot be converted.
            migrationBuilder.Sql("DELETE FROM [Documents];");

            // SQL Server cannot change nullability of a column used by a foreign key or index.
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IncomingPayload",
                table: "Documents");

            migrationBuilder.RenameColumn(
                name: "ReceivedAtUtc",
                table: "Documents",
                newName: "LastReportedAtUtc");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgentId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" },
                principalTable: "Agents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationNumber",
                table: "Documents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertifiedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "Documents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Documents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "Documents",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IssuedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Documents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResendRequestedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Series",
                table: "Documents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Documents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_CompanyId_Status_IssuedAtUtc",
                table: "Documents",
                columns: new[] { "TenantId", "CompanyId", "Status", "IssuedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_AttemptCount",
                table: "Documents",
                sql: "[AttemptCount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_CompanyId_Status_IssuedAtUtc",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_AttemptCount",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AuthorizationNumber",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "CertifiedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IssuedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ResendRequestedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Series",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Documents");

            migrationBuilder.RenameColumn(
                name: "LastReportedAtUtc",
                table: "Documents",
                newName: "ReceivedAtUtc");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgentId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Agents_TenantId_AgentId",
                table: "Documents",
                columns: new[] { "TenantId", "AgentId" },
                principalTable: "Agents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddColumn<string>(
                name: "IncomingPayload",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DocumentDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorDetails = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDeliveryAttempts", x => x.Id);
                    table.CheckConstraint("CK_DocumentDeliveryAttempts_AttemptNumber", "[AttemptNumber] > 0");
                    table.ForeignKey(
                        name: "FK_DocumentDeliveryAttempts_Documents_DocumentSubmissionId",
                        column: x => x.DocumentSubmissionId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDeliveryAttempts_DocumentSubmissionId_AttemptNumber",
                table: "DocumentDeliveryAttempts",
                columns: new[] { "DocumentSubmissionId", "AttemptNumber" },
                unique: true);
        }
    }
}
