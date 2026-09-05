using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeasonOnGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeasonType",
                table: "Games",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SeasonYear",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Games_SportId_SeasonYear_StartsAt",
                table: "Games",
                columns: new[] { "SportId", "SeasonYear", "StartsAt" });

            // Rows written before the column existed are stamped by rule. The rule is the one
            // SeasonCalendar applies when a provider gives no stamp, restated in SQL so the two
            // cannot disagree: football and the winter sports straddle the year and are named by
            // the year they start; baseball is a calendar year. Going forward ESPN stamps every
            // event itself and the resolver overwrites these where they differ.
            migrationBuilder.Sql("""
                UPDATE "Games" g
                SET "SeasonYear" = CASE
                        WHEN s."Key" IN ('nfl', 'ncaaf') THEN
                            CASE WHEN EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') < 3
                                 THEN EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') - 1
                                 ELSE EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') END
                        WHEN s."Key" IN ('nba', 'nhl', 'ncaab') THEN
                            CASE WHEN EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') < 9
                                 THEN EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') - 1
                                 ELSE EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') END
                        ELSE EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC')
                    END,
                    "SeasonType" = CASE
                        -- NFL: kickoff is the Thursday after Labor Day (first Monday of
                        -- September); the regular season ends 17 weeks and 4 days later, on
                        -- the Monday of week 18. Later in that season is the postseason.
                        WHEN s."Key" = 'nfl' AND (g."StartsAt" AT TIME ZONE 'UTC')::date > (
                                 make_date(
                                     (CASE WHEN EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') < 3
                                           THEN EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') - 1
                                           ELSE EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') END)::int, 9, 1)
                                 + ((1 - EXTRACT(DOW FROM make_date(
                                     (CASE WHEN EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') < 3
                                           THEN EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') - 1
                                           ELSE EXTRACT(YEAR FROM g."StartsAt" AT TIME ZONE 'UTC') END)::int, 9, 1))::int + 7) % 7)
                                 + 3 + 17 * 7 + 4)
                            THEN 'Postseason'
                        WHEN s."Key" = 'mlb' AND EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') >= 10
                            THEN 'Postseason'
                        WHEN s."Key" IN ('nba', 'nhl') AND (
                                 EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') IN (5, 6)
                              OR (EXTRACT(MONTH FROM g."StartsAt" AT TIME ZONE 'UTC') = 4
                                  AND EXTRACT(DAY FROM g."StartsAt" AT TIME ZONE 'UTC') >= 15))
                            THEN 'Postseason'
                        ELSE 'Regular'
                    END
                FROM "Sports" s
                WHERE s."Id" = g."SportId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_SportId_SeasonYear_StartsAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SeasonType",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SeasonYear",
                table: "Games");
        }
    }
}
