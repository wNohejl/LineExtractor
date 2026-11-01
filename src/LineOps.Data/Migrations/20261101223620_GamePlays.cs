using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class GamePlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameDrives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    SourceDriveId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Yards = table.Column<int>(type: "integer", nullable: true),
                    OffensivePlays = table.Column<int>(type: "integer", nullable: true),
                    TimeElapsed = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsScore = table.Column<bool>(type: "boolean", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    StartPeriod = table.Column<int>(type: "integer", nullable: true),
                    StartClock = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    StartYardLine = table.Column<int>(type: "integer", nullable: true),
                    EndPeriod = table.Column<int>(type: "integer", nullable: true),
                    EndClock = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EndYardLine = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameDrives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameDrives_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameDrives_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameDrives_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GamePlays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    SourcePlayId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    DriveSeq = table.Column<int>(type: "integer", nullable: true),
                    Period = table.Column<int>(type: "integer", nullable: true),
                    Clock = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ClockSeconds = table.Column<int>(type: "integer", nullable: true),
                    TypeId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    HomeScore = table.Column<int>(type: "integer", nullable: true),
                    AwayScore = table.Column<int>(type: "integer", nullable: true),
                    IsScoring = table.Column<bool>(type: "boolean", nullable: false),
                    IsTurnover = table.Column<bool>(type: "boolean", nullable: false),
                    IsPenalty = table.Column<bool>(type: "boolean", nullable: false),
                    Yardage = table.Column<int>(type: "integer", nullable: true),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    StartSituation = table.Column<string>(type: "jsonb", nullable: true),
                    EndSituation = table.Column<string>(type: "jsonb", nullable: true),
                    HomeWinPct = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    SpreadCoverPctHome = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    TotalOverPct = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    Wallclock = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePlays_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePlays_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePlays_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameDrives_SourceId",
                table: "GameDrives",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_GameDrives_TeamId",
                table: "GameDrives",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "ux_game_drive",
                table: "GameDrives",
                columns: new[] { "GameId", "SourceId", "SourceDriveId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_play_game_sequence",
                table: "GamePlays",
                columns: new[] { "GameId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_SourceId",
                table: "GamePlays",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_TeamId",
                table: "GamePlays",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "ux_game_play",
                table: "GamePlays",
                columns: new[] { "GameId", "SourceId", "SourcePlayId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameDrives");

            migrationBuilder.DropTable(
                name: "GamePlays");
        }
    }
}
