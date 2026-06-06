using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_Deduplication",
                table: "Alerts");

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Integrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Deduplication",
                table: "Alerts",
                columns: new[] { "UserId", "Type", "DomainId", "Channel", "DeduplicationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_Deduplication",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Integrations");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Deduplication",
                table: "Alerts",
                columns: new[] { "UserId", "Type", "DomainId", "DeduplicationKey" },
                unique: true);
        }
    }
}
