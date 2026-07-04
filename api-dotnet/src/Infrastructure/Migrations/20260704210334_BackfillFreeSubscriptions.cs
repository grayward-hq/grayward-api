using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillFreeSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Subscriptions" (
                    "Id",
                    "UserId",
                    "Plan",
                    "Status",
                    "CurrentPeriodStart",
                    "CurrentPeriodEnd",
                    "CreatedAt"
                )
                SELECT
                    gen_random_uuid(),
                    u."Id",
                    'Free',
                    'Active',
                    now(),
                    now() + interval '1 month',
                    now()
                FROM "AspNetUsers" AS u
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "Subscriptions" AS s
                    WHERE s."UserId" = u."Id"
                );
                """);
                
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        
        }
    }
}
