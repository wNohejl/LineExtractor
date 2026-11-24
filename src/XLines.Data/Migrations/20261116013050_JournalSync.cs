using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XLines.Data.Migrations
{
    /// <summary>
    /// The journal gets an identity that is the same on every machine, a time it last changed,
    /// a record of deletions, an audit of every write and a log of data moved between machines
    /// (ADR 0025).
    ///
    /// <para>
    /// An entry already held gets a <c>SyncId</c> hashed from its row id and placement time, the
    /// same on both machines that hold a copy of it (see the SQL below). <c>UpdatedAtUtc</c> is set
    /// from what the row says about itself — settled, else placed — rather than left at the
    /// moment of migration: two machines that restored the same journal and migrated at
    /// different times must still agree that neither copy is newer. Settings say nothing about
    /// when they were written, so they take one fixed moment older than any real write.
    /// </para>
    /// </summary>
    public partial class JournalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Parlays",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "JournalEntries",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "JournalEntries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "AppSettings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // The entries already held: an identity derived from what both machines share, not a
            // random one. The desktop and the laptop each run this migration over the same journal
            // (yesterday's journal.dump carried the row ids and placement times across), and a
            // random uuid on each would make every existing bet two different bets to the merge.
            // The row id and the moment of placement, to the microsecond, are the same on both
            // copies of one bet and differ between any two bets, so the hash of the two is.
            // New rows take a random one (the default above, and JournalEntry's initialiser).
            migrationBuilder.Sql("""
                UPDATE "JournalEntries" SET
                  "SyncId" = md5('lineops-journal-entry:' || "Id" || ':'
                                 || to_char("PlacedAt" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US'))::uuid,
                  "UpdatedAtUtc" = coalesce("SettledAt", "PlacedAt");
                UPDATE "Parlays" SET "UpdatedAtUtc" = coalesce("SettledAt", "PlacedAt");
                UPDATE "AppSettings" SET "UpdatedAtUtc" = '2000-01-01T00:00:00Z';
                """);

            migrationBuilder.CreateTable(
                name: "DataTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Machine = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataTransfers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalAudit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntrySyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    Entity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Machine = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalTombstones",
                columns: table => new
                {
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalTombstones", x => x.SyncId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SyncId",
                table: "JournalEntries",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataTransfers_AtUtc",
                table: "DataTransfers",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_EntrySyncId_AtUtc",
                table: "JournalAudit",
                columns: new[] { "EntrySyncId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataTransfers");

            migrationBuilder.DropTable(
                name: "JournalAudit");

            migrationBuilder.DropTable(
                name: "JournalTombstones");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_SyncId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Parlays");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "AppSettings");
        }
    }
}
