using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClosingLinesAndCrossReferenceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                newName: "ix_player_team");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerGameStats_GameId",
                table: "PlayerGameStats",
                newName: "ix_player_game_stat_game");

            migrationBuilder.CreateTable(
                name: "ClosingLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Book = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Market = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Line = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PriceAmerican = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PromotedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosingLines_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClosingLines_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClosingLines_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosingLines_PlayerId",
                table: "ClosingLines",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingLines_SourceId",
                table: "ClosingLines",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ux_closing_line",
                table: "ClosingLines",
                columns: new[] { "GameId", "Book", "Market", "Outcome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosingLines");

            migrationBuilder.RenameIndex(
                name: "ix_player_team",
                table: "Players",
                newName: "IX_Players_TeamId");

            migrationBuilder.RenameIndex(
                name: "ix_player_game_stat_game",
                table: "PlayerGameStats",
                newName: "IX_PlayerGameStats_GameId");
        }
    }
}
