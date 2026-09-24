using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class ParlaysAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SettledAt",
                table: "JournalEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Parlays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Book = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Stake = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PriceQuoted = table.Column<int>(type: "integer", nullable: true),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Payout = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parlays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parlays_PlacedAt",
                table: "Parlays",
                column: "PlacedAt");

            // Group ids written before parlays had a row of their own point at nothing. Those
            // entries were graded and counted as straight bets all along, so that is what they
            // become, rather than blocking the key.
            migrationBuilder.Sql(
                "UPDATE \"JournalEntries\" SET \"ParlayGroupId\" = NULL WHERE \"ParlayGroupId\" IS NOT NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Parlays_ParlayGroupId",
                table: "JournalEntries",
                column: "ParlayGroupId",
                principalTable: "Parlays",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_Parlays_ParlayGroupId",
                table: "JournalEntries");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "Parlays");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "JournalEntries");
        }
    }
}
