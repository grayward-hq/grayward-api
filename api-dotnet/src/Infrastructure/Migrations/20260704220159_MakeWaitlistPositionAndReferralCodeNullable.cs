using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeWaitlistPositionAndReferralCodeNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReferralCode",
                table: "Waitlists",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                collation: "case_insensitive",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldCollation: "case_insensitive");

            migrationBuilder.AlterColumn<long>(
                name: "Position",
                table: "Waitlists",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReferralCode",
                table: "Waitlists",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                collation: "case_insensitive",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldCollation: "case_insensitive");

            migrationBuilder.AlterColumn<long>(
                name: "Position",
                table: "Waitlists",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
