using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppearanceTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "PlayerGameStats",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_game_stat_team",
                table: "PlayerGameStats",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerGameStats_Teams_TeamId",
                table: "PlayerGameStats",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerGameStats_Teams_TeamId",
                table: "PlayerGameStats");

            migrationBuilder.DropIndex(
                name: "ix_player_game_stat_team",
                table: "PlayerGameStats");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "PlayerGameStats");
        }
    }
}
