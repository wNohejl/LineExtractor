using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManualPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManualPrice",
                table: "JournalEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualPrice",
                table: "JournalEntries");
        }
    }
}
