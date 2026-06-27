using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoredRepositoryAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoredRepositories_RepoId",
                table: "MonitoredRepositories");

            migrationBuilder.DropColumn(
                name: "GitHubInstallationId",
                table: "MonitoredRepositories");

            migrationBuilder.RenameColumn(
                name: "IsMonitoringActive",
                table: "MonitoredRepositories",
                newName: "IsPrivate");

            migrationBuilder.AddColumn<string>(
                name: "CloneUrl",
                table: "MonitoredRepositories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InstallationId",
                table: "MonitoredRepositories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastScanCompletedAt",
                table: "MonitoredRepositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MonitoredRepositories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RepositorySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodicScanEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PeriodicScanFrequency = table.Column<int>(type: "integer", nullable: false),
                    NextScanDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastScanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventScanEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Triggers = table.Column<int>(type: "integer", nullable: false),
                    AlertChannels = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositorySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositorySettings_MonitoredRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "MonitoredRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plan = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CurrentPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredRepositories_UserId_RepoId",
                table: "MonitoredRepositories",
                columns: new[] { "UserId", "RepoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositorySettings_RepositoryId",
                table: "RepositorySettings",
                column: "RepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CurrentPeriodEnd",
                table: "Subscriptions",
                column: "CurrentPeriodEnd");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId_Status",
                table: "Subscriptions",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepositorySettings");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_MonitoredRepositories_UserId_RepoId",
                table: "MonitoredRepositories");

            migrationBuilder.DropColumn(
                name: "CloneUrl",
                table: "MonitoredRepositories");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                table: "MonitoredRepositories");

            migrationBuilder.DropColumn(
                name: "LastScanCompletedAt",
                table: "MonitoredRepositories");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MonitoredRepositories");

            migrationBuilder.RenameColumn(
                name: "IsPrivate",
                table: "MonitoredRepositories",
                newName: "IsMonitoringActive");

            migrationBuilder.AddColumn<long>(
                name: "GitHubInstallationId",
                table: "MonitoredRepositories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredRepositories_RepoId",
                table: "MonitoredRepositories",
                column: "RepoId",
                unique: true);
        }
    }
}
