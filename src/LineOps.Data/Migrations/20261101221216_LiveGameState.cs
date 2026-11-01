using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class LiveGameState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AwayTimeouts",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClockSeconds",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayClock",
                table: "Games",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Distance",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Down",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownDistanceText",
                table: "Games",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeTimeouts",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRedZone",
                table: "Games",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPlay",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Linescore",
                table: "Games",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveUpdatedAt",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PossessionTeamId",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PossessionText",
                table: "Games",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Situation",
                table: "Games",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusDetail",
                table: "Games",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YardLine",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_PossessionTeamId",
                table: "Games",
                column: "PossessionTeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Teams_PossessionTeamId",
                table: "Games",
                column: "PossessionTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Teams_PossessionTeamId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_PossessionTeamId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "AwayTimeouts",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ClockSeconds",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DisplayClock",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Distance",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Down",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DownDistanceText",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HomeTimeouts",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IsRedZone",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LastPlay",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Linescore",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LiveUpdatedAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PossessionTeamId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PossessionText",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Situation",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "StatusDetail",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "YardLine",
                table: "Games");
        }
    }
}
