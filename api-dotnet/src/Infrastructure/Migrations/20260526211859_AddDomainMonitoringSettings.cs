using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainMonitoringSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DomainSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DomainId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonitoringEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScanFrequency = table.Column<string>(type: "text", nullable: false),
                    SslAlertThresholds = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NotificationChannel = table.Column<int>(type: "integer", nullable: false),
                    LastMonitoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainSettings_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DomainSettings_DomainId",
                table: "DomainSettings",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainSettings_DueForScan",
                table: "DomainSettings",
                columns: new[] { "MonitoringEnabled", "NextScheduledAt" },
                filter: "\"MonitoringEnabled\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DomainSettings");
        }
    }
}
