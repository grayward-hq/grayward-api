using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwaspMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OWASPPostureSummary",
                table: "Scans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OWASPScore",
                table: "Scans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OWASPTier",
                table: "Scans",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwaspMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FindingLabel = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwaspMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwaspMappings_Findings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "Findings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OwaspMappings_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwaspMappings_FindingId",
                table: "OwaspMappings",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_OwaspMappings_ScanId_FindingId_CategoryCode",
                table: "OwaspMappings",
                columns: new[] { "ScanId", "FindingId", "CategoryCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwaspMappings");

            migrationBuilder.DropColumn(
                name: "OWASPPostureSummary",
                table: "Scans");

            migrationBuilder.DropColumn(
                name: "OWASPScore",
                table: "Scans");

            migrationBuilder.DropColumn(
                name: "OWASPTier",
                table: "Scans");
        }
    }
}
