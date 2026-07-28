using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LineOps.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Timeline = table.Column<string>(type: "jsonb", nullable: false),
                    RootCause = table.Column<string>(type: "text", nullable: true),
                    CorrectiveActions = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RateLimitPerHour = table.Column<int>(type: "integer", nullable: true),
                    RateLimitPerDay = table.Column<int>(type: "integer", nullable: true),
                    MonthlyCreditBudget = table.Column<int>(type: "integer", nullable: true),
                    FailureMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IncidentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Alerts_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IngestionRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    JobKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RowsIngested = table.Column<int>(type: "integer", nullable: false),
                    RequestsMade = table.Column<int>(type: "integer", nullable: false),
                    CreditsSpent = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestionRuns_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KpiDailies",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    FreshnessMinutes = table.Column<double>(type: "double precision", nullable: false),
                    SuccessRate = table.Column<double>(type: "double precision", nullable: false),
                    RowsIngested = table.Column<int>(type: "integer", nullable: false),
                    RunCount = table.Column<int>(type: "integer", nullable: false),
                    ApiCreditsUsed = table.Column<int>(type: "integer", nullable: false),
                    RequestsMade = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiDailies", x => new { x.Day, x.SourceId });
                    table.ForeignKey(
                        name: "FK_KpiDailies_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SportId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Abbrev = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalIds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SportId = table.Column<int>(type: "integer", nullable: false),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: false),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HomeScore = table.Column<int>(type: "integer", nullable: true),
                    AwayScore = table.Column<int>(type: "integer", nullable: true),
                    ExternalIds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Games_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SportId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Position = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalIds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Players_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StatSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestionRunId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatSnapshots_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StatSnapshots_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    Market = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: true),
                    FreeTextMarket = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Book = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LineTaken = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PriceTaken = table.Column<int>(type: "integer", nullable: false),
                    Stake = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Payout = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ClosingSnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    ClosingCapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosingPrice = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ParlayGroupId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            // OddsSnapshots is created by hand rather than through CreateTable because it is a
            // native range-partitioned table. Line history is the one table that grows without
            // bound — every poll appends a row per book/market/outcome — so it is partitioned
            // by month on CapturedAt. Old months can then be detached or dropped as a metadata
            // operation instead of a mass DELETE, and queries for a single game's movement are
            // pruned to the partitions that can actually contain it.
            migrationBuilder.Sql("""
                CREATE TABLE "OddsSnapshots" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "CapturedAt" timestamp with time zone NOT NULL,
                    "GameId" integer NOT NULL,
                    "SourceId" integer NOT NULL,
                    "Book" character varying(64) NOT NULL,
                    "Market" character varying(64) NOT NULL,
                    "Outcome" character varying(128) NOT NULL,
                    "Line" numeric(10,2) NULL,
                    "PriceAmerican" integer NOT NULL,
                    "PlayerId" integer NULL,
                    "IngestionRunId" bigint NOT NULL,
                    CONSTRAINT "PK_OddsSnapshots" PRIMARY KEY ("CapturedAt", "Id"),
                    CONSTRAINT "FK_OddsSnapshots_Games_GameId" FOREIGN KEY ("GameId")
                        REFERENCES "Games" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_OddsSnapshots_Players_PlayerId" FOREIGN KEY ("PlayerId")
                        REFERENCES "Players" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_OddsSnapshots_Sources_SourceId" FOREIGN KEY ("SourceId")
                        REFERENCES "Sources" ("Id") ON DELETE CASCADE
                ) PARTITION BY RANGE ("CapturedAt");
                """);

            // Creates the monthly partition covering a given timestamp, if absent. The worker
            // calls this ahead of each run so a month boundary never turns into a failed insert.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION lineops_ensure_odds_partition(target timestamptz)
                RETURNS void AS $$
                DECLARE
                    period_start date := date_trunc('month', target AT TIME ZONE 'UTC')::date;
                    period_end   date := (date_trunc('month', target AT TIME ZONE 'UTC') + interval '1 month')::date;
                    partition_name text := format('OddsSnapshots_%s', to_char(period_start, 'YYYY_MM'));
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_class WHERE relname = partition_name) THEN
                        EXECUTE format(
                            'CREATE TABLE %I PARTITION OF "OddsSnapshots" FOR VALUES FROM (%L) TO (%L)',
                            partition_name, period_start, period_end);
                    END IF;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // Seed the current month and its neighbours, then a DEFAULT partition so a row can
            // never be rejected for falling outside every declared range.
            migrationBuilder.Sql("""
                SELECT lineops_ensure_odds_partition(now() - interval '1 month');
                SELECT lineops_ensure_odds_partition(now());
                SELECT lineops_ensure_odds_partition(now() + interval '1 month');
                CREATE TABLE "OddsSnapshots_default" PARTITION OF "OddsSnapshots" DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "PlayerGameStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<int>(type: "integer", nullable: false),
                    StatLine = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestionRunId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGameStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerGameStats_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerGameStats_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerGameStats_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_IncidentId",
                table: "Alerts",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_RuleKey_SourceId_ResolvedAt",
                table: "Alerts",
                columns: new[] { "RuleKey", "SourceId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SourceId",
                table: "Alerts",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_AwayTeamId",
                table: "Games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_HomeTeamId",
                table: "Games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_SportId_StartsAt",
                table: "Games",
                columns: new[] { "SportId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRuns_SourceId_StartedAt",
                table: "IngestionRuns",
                columns: new[] { "SourceId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_GameId",
                table: "JournalEntries",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ParlayGroupId",
                table: "JournalEntries",
                column: "ParlayGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PlacedAt",
                table: "JournalEntries",
                column: "PlacedAt");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PlayerId",
                table: "JournalEntries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiDailies_SourceId",
                table: "KpiDailies",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ix_odds_snapshot_game_market_book_captured",
                table: "OddsSnapshots",
                columns: new[] { "GameId", "Market", "Book", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OddsSnapshots_PlayerId",
                table: "OddsSnapshots",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_OddsSnapshots_SourceId",
                table: "OddsSnapshots",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ux_odds_snapshot_natural_key",
                table: "OddsSnapshots",
                columns: new[] { "GameId", "SourceId", "Book", "Market", "Outcome", "CapturedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameStats_GameId",
                table: "PlayerGameStats",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameStats_SourceId",
                table: "PlayerGameStats",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "ux_player_game_stat",
                table: "PlayerGameStats",
                columns: new[] { "PlayerId", "GameId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SportId_FullName",
                table: "Players",
                columns: new[] { "SportId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_Key",
                table: "Sources",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sports_Key",
                table: "Sports",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatSnapshots_GameId_CapturedAt",
                table: "StatSnapshots",
                columns: new[] { "GameId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StatSnapshots_SourceId",
                table: "StatSnapshots",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SportId_Name",
                table: "Teams",
                columns: new[] { "SportId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "IngestionRuns");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "KpiDailies");

            migrationBuilder.DropTable(
                name: "OddsSnapshots");

            migrationBuilder.DropTable(
                name: "PlayerGameStats");

            migrationBuilder.DropTable(
                name: "StatSnapshots");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Sources");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Sports");
        }
    }
}
