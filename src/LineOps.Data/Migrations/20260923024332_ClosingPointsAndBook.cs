using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClosingPointsAndBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosingBook",
                table: "JournalEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingPoints",
                table: "JournalEntries",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingBook",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ClosingPoints",
                table: "JournalEntries");
        }
    }
}
