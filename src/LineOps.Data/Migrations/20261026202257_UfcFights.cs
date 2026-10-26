using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class UfcFights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bouts",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventShortName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CardSegment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MatchNumber = table.Column<int>(type: "integer", nullable: true),
                    WeightClass = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    ScheduledRounds = table.Column<int>(type: "integer", nullable: false),
                    TitleFight = table.Column<bool>(type: "boolean", nullable: false),
                    HomeRecord = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AwayRecord = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MethodDetail = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    EndRound = table.Column<int>(type: "integer", nullable: true),
                    EndSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bouts", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_Bouts_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FighterProfiles",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Association = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Stance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WeightClass = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    HeightInches = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    ReachInches = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    BornOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Record = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Career = table.Column<string>(type: "jsonb", nullable: false),
                    History = table.Column<string>(type: "jsonb", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FighterProfiles", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_FighterProfiles_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bouts_EventId",
                table: "Bouts",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bouts");

            migrationBuilder.DropTable(
                name: "FighterProfiles");
        }
    }
}
