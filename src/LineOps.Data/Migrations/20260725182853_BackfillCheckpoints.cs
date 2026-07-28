using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackfillCheckpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    SportId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GamesFound = table.Column<int>(type: "integer", nullable: false),
                    RowsIngested = table.Column<int>(type: "integer", nullable: false),
                    RequestsMade = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackfillCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackfillCheckpoints_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BackfillCheckpoints_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackfillCheckpoints_SourceId_Date",
                table: "BackfillCheckpoints",
                columns: new[] { "SourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BackfillCheckpoints_SportId",
                table: "BackfillCheckpoints",
                column: "SportId");

            migrationBuilder.CreateIndex(
                name: "ux_backfill_checkpoint",
                table: "BackfillCheckpoints",
                columns: new[] { "SourceId", "SportId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackfillCheckpoints");
        }
    }
}
