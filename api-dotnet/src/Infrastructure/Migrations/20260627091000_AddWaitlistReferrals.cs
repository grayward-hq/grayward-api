using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Define the case-insensitive ICU collation before any column references it.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-primary,und-u-ks-primary,icu,False");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReferralAt",
                table: "Waitlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "Waitlists",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                collation: "case_insensitive");

            migrationBuilder.AddColumn<int>(
                name: "ReferralCount",
                table: "Waitlists",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferredByWaitlistId",
                table: "Waitlists",
                type: "uuid",
                nullable: true);

            // Backfill referral codes for existing rows before enforcing NOT NULL.
            migrationBuilder.Sql("""
                UPDATE "Waitlists"
                SET "ReferralCode" = UPPER(REPLACE("Id"::text, '-', ''))
                WHERE "ReferralCode" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ReferralCode",
                table: "Waitlists",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                collation: "case_insensitive",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            // Apply the case-insensitive collation to the existing Email column and its index.
            migrationBuilder.DropIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Waitlists",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                collation: "case_insensitive",
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists",
                column: "Email",
                unique: true)
                .Annotation("Relational:Collation", new[] { "case_insensitive" });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_ReferralCode",
                table: "Waitlists",
                column: "ReferralCode",
                unique: true)
                .Annotation("Relational:Collation", new[] { "case_insensitive" });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_ReferredByWaitlistId",
                table: "Waitlists",
                column: "ReferredByWaitlistId");

            migrationBuilder.AddForeignKey(
                name: "FK_Waitlists_Waitlists_ReferredByWaitlistId",
                table: "Waitlists",
                column: "ReferredByWaitlistId",
                principalTable: "Waitlists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Waitlists_Waitlists_ReferredByWaitlistId",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_ReferralCode",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_ReferredByWaitlistId",
                table: "Waitlists");

            // Revert the Email column collation and its index.
            migrationBuilder.DropIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Waitlists",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldCollation: "case_insensitive");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists",
                column: "Email",
                unique: true);

            migrationBuilder.DropColumn(
                name: "LastReferralAt",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "ReferralCount",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "ReferredByWaitlistId",
                table: "Waitlists");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-primary,und-u-ks-primary,icu,False");
        }
    }
}
