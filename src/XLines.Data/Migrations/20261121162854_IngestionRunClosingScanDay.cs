using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XLines.Data.Migrations
{
    /// <summary>
    /// A run says when it was a closing scan, and which league day it counts against, so the cap
    /// on closing scans a league a day is counted from the runs table rather than held in memory,
    /// where a restart started it again. Runs already held are left null: none of them was marked,
    /// and today's count starting from zero once is what a restart did before.
    /// </summary>
    public partial class IngestionRunClosingScanDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ClosingScanDay",
                table: "IngestionRuns",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingScanDay",
                table: "IngestionRuns");
        }
    }
}
