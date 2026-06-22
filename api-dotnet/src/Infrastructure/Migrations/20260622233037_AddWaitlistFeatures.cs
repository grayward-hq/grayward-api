using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Waitlists",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CompanyName",
                table: "Waitlists",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comments",
                table: "Waitlists",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailConfirmationToken",
                table: "Waitlists",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "Waitlists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmedAt",
                table: "Waitlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitationToken",
                table: "Waitlists",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Waitlists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Position",
                table: "Waitlists",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotedAt",
                table: "Waitlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotedUserId",
                table: "Waitlists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Waitlists",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_CreatedAt",
                table: "Waitlists",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_Position",
                table: "Waitlists",
                column: "Position",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_PromotedUserId",
                table: "Waitlists",
                column: "PromotedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlists_Status",
                table: "Waitlists",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Waitlists_CreatedAt",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_Email",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_Position",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_PromotedUserId",
                table: "Waitlists");

            migrationBuilder.DropIndex(
                name: "IX_Waitlists_Status",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "Comments",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "EmailConfirmationToken",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "EmailConfirmedAt",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "InvitationToken",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "PromotedAt",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "PromotedUserId",
                table: "Waitlists");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Waitlists");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Waitlists",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "CompanyName",
                table: "Waitlists",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
