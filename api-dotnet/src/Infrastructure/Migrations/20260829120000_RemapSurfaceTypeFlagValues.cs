using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Rewrites stored SurfaceTypes masks after the flag values were corrected.
    /// </summary>
    /// <remarks>
    /// Http was 3, which is Dns | Ssl, so the two overlapped and the number 3 was ambiguous.
    /// HttpHeaders is now 4, so every stored value has to be re-expressed under the new numbering.
    ///
    /// Production held exactly two values: 0 (25 rows) and 3 (274 rows).
    ///
    /// The only code that ever produced a combination is ScanDispatchService, which asked for
    /// Dns | Ssl | Http. Under the old values that is 1 | 2 | 3 = 3, because 3 already contained
    /// both other bits. So a stored 3 means "Dns and Ssl and Http were all requested", and its
    /// faithful translation is Dns | Ssl | HttpHeaders = 1 | 2 | 4 = 7.
    ///
    /// Mapping 3 to 4 would read it as "HttpHeaders only" and silently drop Dns and Ssl from 274
    /// historical scans, which is why this maps to 7 instead.
    ///
    /// 0 is None and stays 0: those rows predate the validator that now rejects an empty mask.
    /// </remarks>
    public partial class RemapSurfaceTypeFlagValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""Scans"" SET ""SurfaceTypes"" = 7 WHERE ""SurfaceTypes"" = 3;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""Scans"" SET ""SurfaceTypes"" = 3 WHERE ""SurfaceTypes"" = 7;");
        }
    }
}