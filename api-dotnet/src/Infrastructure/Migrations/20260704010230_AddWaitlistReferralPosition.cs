using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistReferralPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReferralPosition",
                table: "Waitlists",
                type: "bigint",
                nullable: true);

            // Seed the referral ranking for existing rows: fixed base of 40 minus referrals
            // earned, floored at 1. New rows start at 40 from the application.
            migrationBuilder.Sql("""
                UPDATE "Waitlists"
                SET "ReferralPosition" = GREATEST(40 - "ReferralCount", 1)
                WHERE "ReferralPosition" IS NULL;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "ReferralPosition",
                table: "Waitlists",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_ReferralPosition",
                table: "Waitlists",
                column: "ReferralPosition");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Waitlists_ReferralPosition",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "ReferralPosition",
                table: "Waitlists");
        }
    }
}
