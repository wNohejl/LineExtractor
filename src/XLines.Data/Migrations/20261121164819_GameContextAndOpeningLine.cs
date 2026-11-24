using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XLines.Data.Migrations
{
    /// <summary>
    /// What the adapters already download and threw away: the opening line, on the close's own row
    /// beside it (null on every close held, until a walk states one), and a game's circumstances —
    /// venue, weather, crowd, the provider's win probability and MLB's lineups in GameContexts, one
    /// row per game and source, and the officials in GameOfficials, one row each, so a referee or
    /// umpire can be asked about across games. Nothing is backfilled: every row held stays as it was.
    /// </summary>
    public partial class GameContextAndOpeningLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OpenLine",
                table: "ClosingLines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenPriceAmerican",
                table: "ClosingLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameContexts",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    VenueName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VenueCity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VenueState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Indoor = table.Column<bool>(type: "boolean", nullable: true),
                    Surface = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    WeatherCondition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TemperatureF = table.Column<int>(type: "integer", nullable: true),
                    WindSpeedMph = table.Column<int>(type: "integer", nullable: true),
                    WindDirection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PrecipitationPercent = table.Column<int>(type: "integer", nullable: true),
                    Attendance = table.Column<int>(type: "integer", nullable: true),
                    DayNight = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    HomeWinPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    AwayWinPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    MatchupQuality = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    HomeLineup = table.Column<string>(type: "jsonb", nullable: true),
                    AwayLineup = table.Column<string>(type: "jsonb", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameContexts", x => new { x.GameId, x.SourceId });
                    table.ForeignKey(
                        name: "FK_GameContexts_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameContexts_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameOfficials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameOfficials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameOfficials_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameOfficials_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameContexts_SourceId",
                table: "GameContexts",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ix_game_official_name",
                table: "GameOfficials",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_GameOfficials_SourceId",
                table: "GameOfficials",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ux_game_official",
                table: "GameOfficials",
                columns: new[] { "GameId", "SourceId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameContexts");

            migrationBuilder.DropTable(
                name: "GameOfficials");

            migrationBuilder.DropColumn(
                name: "OpenLine",
                table: "ClosingLines");

            migrationBuilder.DropColumn(
                name: "OpenPriceAmerican",
                table: "ClosingLines");
        }
    }
}
