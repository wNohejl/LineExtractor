using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProbablePitchersAndDoubleHeaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AwayProbablePitcher",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DoubleHeaderGame",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeProbablePitcher",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayProbablePitcher",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DoubleHeaderGame",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HomeProbablePitcher",
                table: "Games");
        }
    }
}
