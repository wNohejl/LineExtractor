using System.Text.Json;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data;

public class LineOpsDbContext(DbContextOptions<LineOpsDbContext> options) : DbContext(options)
{
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Game> Games => Set<Game>();

    public DbSet<OddsSnapshot> OddsSnapshots => Set<OddsSnapshot>();
    public DbSet<ClosingLine> ClosingLines => Set<ClosingLine>();
    public DbSet<StatSnapshot> StatSnapshots => Set<StatSnapshot>();
    public DbSet<PlayerGameStat> PlayerGameStats => Set<PlayerGameStat>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<BackfillCheckpoint> BackfillCheckpoints => Set<BackfillCheckpoint>();
    public DbSet<KpiDaily> KpiDailies => Set<KpiDaily>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var jsonOptions = JsonSerializerOptions.Default;

        // External-id maps are stored as jsonb: cross-source entity resolution needs a
        // bag of per-source keys, and the shape differs by provider.
        var dictConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
            Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, jsonOptions),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, jsonOptions) ?? new());

        var dictComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, string>>(
            (a, c) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(c, jsonOptions),
            v => v == null ? 0 : JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(
                     JsonSerializer.Serialize(v, jsonOptions), jsonOptions) ?? new());

        b.Entity<Sport>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        });

        b.Entity<Team>(e =>
        {
            e.HasIndex(x => new { x.SportId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Abbrev).HasMaxLength(16);
            e.Property(x => x.ExternalIds).HasColumnType("jsonb")
                .HasConversion(dictConverter, dictComparer);
            e.HasOne(x => x.Sport).WithMany(s => s.Teams).HasForeignKey(x => x.SportId);
        });

        b.Entity<Player>(e =>
        {
            e.HasIndex(x => new { x.SportId, x.FullName });

            // The cross-reference starts from a game, walks to its two teams, and needs their
            // players. Without this that first hop is a sequential scan of every player in the
            // league on every odds view.
            e.HasIndex(x => x.TeamId).HasDatabaseName("ix_player_team");

            e.Property(x => x.FullName).HasMaxLength(160).IsRequired();
            e.Property(x => x.Position).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ExternalIds).HasColumnType("jsonb")
                .HasConversion(dictConverter, dictComparer);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Source>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.BaseUrl).HasMaxLength(256);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.FailureMode).HasMaxLength(32);
        });

        b.Entity<Game>(e =>
        {
            e.HasIndex(x => new { x.SportId, x.StartsAt });
            e.HasIndex(x => new { x.SportId, x.SeasonYear, x.StartsAt });
            e.Property(x => x.SeasonType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ExternalIds).HasColumnType("jsonb")
                .HasConversion(dictConverter, dictComparer);
            e.HasOne(x => x.HomeTeam).WithMany().HasForeignKey(x => x.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AwayTeam).WithMany().HasForeignKey(x => x.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OddsSnapshot>(e =>
        {
            // Composite key led by the partition column: Postgres requires the partition
            // key to participate in every unique constraint on a partitioned table.
            e.HasKey(x => new { x.CapturedAt, x.Id });
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.Property(x => x.Book).HasMaxLength(64).IsRequired();
            e.Property(x => x.Market).HasMaxLength(64).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(128).IsRequired();
            e.Property(x => x.Line).HasPrecision(10, 2);

            // The workhorse index: "line movement for this game/market/book, newest first".
            e.HasIndex(x => new { x.GameId, x.Market, x.Book, x.CapturedAt })
                .HasDatabaseName("ix_odds_snapshot_game_market_book_captured");

            // Idempotency: a re-run of the same window must not double-insert.
            e.HasIndex(x => new { x.GameId, x.SourceId, x.Book, x.Market, x.Outcome, x.CapturedAt })
                .IsUnique()
                .HasDatabaseName("ux_odds_snapshot_natural_key");

            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ClosingLine>(e =>
        {
            e.Property(x => x.Book).HasMaxLength(64).IsRequired();
            e.Property(x => x.Market).HasMaxLength(64).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(128).IsRequired();
            e.Property(x => x.Line).HasPrecision(10, 2);

            // One close per game/book/market/outcome. Promotion is idempotent because of this:
            // a second pass over an already-promoted game has nowhere to put a duplicate.
            e.HasIndex(x => new { x.GameId, x.Book, x.Market, x.Outcome })
                .IsUnique()
                .HasDatabaseName("ux_closing_line");

            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<StatSnapshot>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.GameId, x.CapturedAt });
        });

        b.Entity<PlayerGameStat>(e =>
        {
            e.Property(x => x.StatLine).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PlayerId, x.GameId, x.SourceId })
                .IsUnique()
                .HasDatabaseName("ux_player_game_stat");

            // The second hop of the cross-reference: "recent lines for these games". The
            // ux_ index above is led by PlayerId, which answers one player at a time; a
            // matchup asks about fifty at once and filters by game, so it reads this way round
            // instead.
            e.HasIndex(x => x.GameId).HasDatabaseName("ix_player_game_stat_game");

            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId);
        });

        b.Entity<JournalEntry>(e =>
        {
            e.Property(x => x.Market).HasMaxLength(64).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(128).IsRequired();
            e.Property(x => x.Book).HasMaxLength(64).IsRequired();
            e.Property(x => x.FreeTextMarket).HasMaxLength(256);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Stake).HasPrecision(12, 2);
            e.Property(x => x.Payout).HasPrecision(12, 2);
            e.Property(x => x.LineTaken).HasPrecision(10, 2);
            e.HasIndex(x => x.PlacedAt);
            e.HasIndex(x => x.ParlayGroupId);
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.NetReturn);
            e.Ignore(x => x.IsSettled);
        });

        b.Entity<IngestionRun>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.JobKey).HasMaxLength(64);
            e.HasIndex(x => new { x.SourceId, x.StartedAt });
            e.Ignore(x => x.Duration);
        });

        b.Entity<KpiDaily>(e =>
        {
            e.HasKey(x => new { x.Day, x.SourceId });
        });

        b.Entity<BackfillCheckpoint>(e =>
        {
            // The unique key is the whole point: the backfill asks "have I walked this day?"
            // before every fetch, and a duplicate would mean a day fetched twice.
            e.HasIndex(x => new { x.SourceId, x.SportId, x.Date })
                .IsUnique()
                .HasDatabaseName("ux_backfill_checkpoint");

            // "What is still missing, oldest first" — the query the History panel runs.
            e.HasIndex(x => new { x.SourceId, x.Date });

            e.Property(x => x.Error).HasMaxLength(512);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
            e.HasOne(x => x.Sport).WithMany().HasForeignKey(x => x.SportId);
            e.Ignore(x => x.Succeeded);
        });

        b.Entity<Alert>(e =>
        {
            e.Property(x => x.RuleKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Message).HasMaxLength(1024);
            e.HasIndex(x => new { x.RuleKey, x.SourceId, x.ResolvedAt });
            e.Ignore(x => x.IsOpen);
            e.HasOne(x => x.Incident).WithMany(i => i.Alerts).HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Incident>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Timeline).HasColumnType("jsonb");
            e.Ignore(x => x.TimeToResolve);
        });
    }
}
