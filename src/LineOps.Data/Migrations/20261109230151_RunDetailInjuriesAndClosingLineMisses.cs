using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunDetailInjuriesAndClosingLineMisses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InjuryNote",
                table: "Players",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InjuryStatus",
                table: "Players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InjuryUpdatedAt",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "IngestionRuns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClosingLineMisses",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastTriedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingLineMisses", x => new { x.GameId, x.SourceId });
                    table.ForeignKey(
                        name: "FK_ClosingLineMisses_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClosingLineMisses_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosingLineMisses_RetryAt",
                table: "ClosingLineMisses",
                column: "RetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingLineMisses_SourceId",
                table: "ClosingLineMisses",
                column: "SourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosingLineMisses");

            migrationBuilder.DropColumn(
                name: "InjuryNote",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InjuryStatus",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InjuryUpdatedAt",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "IngestionRuns");
        }
    }
}
