using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandThreatsAndCredentialMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextBreachCheckAt",
                table: "DomainSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BrandThreats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    LookAlikeDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    VariationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResolvesViaDns = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RespondedViaHttp = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    HttpTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RedirectsToOriginal = table.Column<bool>(type: "boolean", nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandThreats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandThreats_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonitoredEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    IsBreached = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BreachCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LatestDetectionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoredEmails_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandThreats_DomainId",
                table: "BrandThreats",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandThreats_DomainId_LookAlikeDomain",
                table: "BrandThreats",
                columns: new[] { "DomainId", "LookAlikeDomain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandThreats_DomainId_Status",
                table: "BrandThreats",
                columns: new[] { "DomainId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredEmails_DomainId",
                table: "MonitoredEmails",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredEmails_DomainId_EmailAddress",
                table: "MonitoredEmails",
                columns: new[] { "DomainId", "EmailAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredEmails_UserId",
                table: "MonitoredEmails",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandThreats");

            migrationBuilder.DropTable(
                name: "MonitoredEmails");

            migrationBuilder.DropColumn(
                name: "NextBreachCheckAt",
                table: "DomainSettings");
        }
    }
}
