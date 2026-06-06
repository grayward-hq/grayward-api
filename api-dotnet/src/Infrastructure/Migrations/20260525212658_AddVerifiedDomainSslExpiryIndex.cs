using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedDomainSslExpiryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ScannedDomains_Verified_SslCertExpiry",
                table: "Domains",
                columns: new[] { "VerificationStatus", "SslCertExpiry" },
                filter: "\"VerificationStatus\" = 'Verified' AND \"SslCertExpiry\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScannedDomains_Verified_SslCertExpiry",
                table: "Domains");
        }
    }
}
