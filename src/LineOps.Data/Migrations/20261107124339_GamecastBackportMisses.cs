using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class GamecastBackportMisses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GamecastBackportMisses",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastTriedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamecastBackportMisses", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_GamecastBackportMisses_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamecastBackportMisses_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GamecastBackportMisses_SourceId",
                table: "GamecastBackportMisses",
                column: "SourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GamecastBackportMisses");
        }
    }
}
