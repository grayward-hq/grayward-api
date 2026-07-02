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

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_ReferralCode",
                table: "Waitlists",
                column: "ReferralCode",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

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
        }
    }
}
