using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class JournalPricesToTheCent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PriceQuoted",
                table: "Parlays",
                type: "numeric(11,4)",
                precision: 11,
                scale: 4,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PriceTaken",
                table: "JournalEntries",
                type: "numeric(11,4)",
                precision: 11,
                scale: 4,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PriceQuoted",
                table: "Parlays",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(11,4)",
                oldPrecision: 11,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PriceTaken",
                table: "JournalEntries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(11,4)",
                oldPrecision: 11,
                oldScale: 4);
        }
    }
}
