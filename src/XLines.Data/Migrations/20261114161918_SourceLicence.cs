using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XLines.Data.Migrations
{
    /// <summary>
    /// Every source says what its terms allow (ADR 0024). Existing rows default to the safe
    /// answer, personal use, and the two providers whose terms allow display are then named by
    /// key, so a database restored from a snapshot made before this column is classified the
    /// same way a fresh one is seeded (<c>SourceLicences.Known</c>).
    /// </summary>
    public partial class SourceLicence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Licence",
                table: "Sources",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Not EF's empty string: an empty name reads as no enum member at all.
                defaultValue: "PersonalUse");

            // The Odds API and balldontlie allow display; ESPN, the MLB Stats API and
            // odds-api.io's free tier do not, and keep the default.
            migrationBuilder.Sql("""
                UPDATE "Sources" SET "Licence" = 'Displayable'
                WHERE "Key" IN ('the-odds-api', 'balldontlie');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Licence",
                table: "Sources");
        }
    }
}
