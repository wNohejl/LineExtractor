using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Turns the odds stream into a permanent record and then throws the stream away.
///
/// <para>
/// Odds are scanned continuously but are only worth keeping once: at first pitch, when the
/// line is set. Before that the market is live and the scans are working state — you want to
/// see where the price is now and how it has moved today, and none of that is worth carrying
/// for ever. After that the game is under way and the scan tier has nothing left to say,
/// because what the market concluded is a single number per book and market.
/// </para>
///
/// <para>So this runs two passes, in order, and the order matters:</para>
/// <list type="number">
///   <item><b>Promote.</b> For every started game with no close recorded, take the newest scan
///   at or before its start time, per book/market/outcome, and write it to
///   <see cref="ClosingLine"/>.</item>
///   <item><b>Prune.</b> Delete the scans for games whose close is now recorded, and
///   everything older than the configured floor regardless.</item>
/// </list>
///
/// <para>
/// Promotion before pruning is what makes this safe to run at any time and safe to interrupt:
/// nothing is deleted until the thing that replaces it exists, and a crash between the two
/// passes leaves scans that the next run will prune. It cannot lose a close.
/// </para>
/// </summary>
public class OddsRetentionService(
    LineOpsDbContext db,
    IOptions<IngestionOptions> options,
    ILogger<OddsRetentionService> logger)
{
    private readonly OddsRetentionOptions _settings = options.Value.OddsRetention;

    /// <summary>Promote, then prune. Returns what each pass did, for the Ops surfaces.</summary>
    public async Task<RetentionReport> RunAsync(CancellationToken ct = default)
    {
        var promoted = await PromoteAsync(ct);
        var pruned = await PruneAsync(ct);

        if (promoted > 0 || pruned > 0)
        {
            logger.LogInformation(
                "Odds retention: {Promoted} closing lines promoted, {Pruned} scans dropped",
                promoted, pruned);
        }

        return new RetentionReport(promoted, pruned);
    }

    /// <summary>
    /// Records the closing line for games that have started since the last pass.
    /// </summary>
    public async Task<int> PromoteAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // A game qualifies once it has started. The grace period covers a book that is still
        // being scanned as the game begins — promoting the instant the clock ticks over would
        // sometimes take a line a few seconds stale over the one that arrives right after.
        var cutoff = now - _settings.PromoteAfterStart;

        var started = await db.Games
            .Where(g => g.StartsAt <= cutoff)
            .Where(g => !db.ClosingLines.Any(c => c.GameId == g.Id))
            .Where(g => db.OddsSnapshots.Any(s => s.GameId == g.Id))
            .Select(g => new { g.Id, g.StartsAt })
            .Take(_settings.PromoteBatchSize)
            .ToListAsync(ct);

        if (started.Count == 0)
            return 0;

        var written = 0;

        foreach (var game in started)
        {
            ct.ThrowIfCancellationRequested();

            // Newest observation at or before the start, per book/market/outcome. Scans taken
            // after the first pitch are in-play prices and are not the close.
            var closes = await db.OddsSnapshots
                .Where(s => s.GameId == game.Id && s.CapturedAt <= game.StartsAt)
                .GroupBy(s => new { s.Book, s.Market, s.Outcome })
                .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
                .ToListAsync(ct);

            foreach (var close in closes)
            {
                db.ClosingLines.Add(new ClosingLine
                {
                    GameId = close.GameId,
                    SourceId = close.SourceId,
                    Book = close.Book,
                    Market = close.Market,
                    Outcome = close.Outcome,
                    Line = close.Line,
                    PriceAmerican = close.PriceAmerican,
                    PlayerId = close.PlayerId,
                    CapturedAt = close.CapturedAt,
                    PromotedAt = now
                });

                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>
    /// Drops scans that are no longer working state. Two rules, and a game must satisfy either
    /// one to lose its scans:
    ///
    /// <list type="bullet">
    ///   <item>its close has been recorded, so the stream has been distilled;</item>
    ///   <item>it was captured before <see cref="OddsRetentionOptions.HistoryFloor"/>, which is
    ///   the date this policy took effect and before which no odds are kept at all.</item>
    /// </list>
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var floor = new DateTimeOffset(
            _settings.HistoryFloor.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Everything before the floor goes, whether or not it was ever promoted. This is the
        // one-off cost of adopting the policy: odds gathered before it existed have no closing
        // line to promote to and no live game to inform.
        var stale = await db.OddsSnapshots
            .Where(s => s.CapturedAt < floor)
            .ExecuteDeleteAsync(ct);

        // Scans for games whose close is on record. Bounded per pass so a first run against a
        // large table cannot hold one enormous transaction.
        var settled = await db.OddsSnapshots
            .Where(s => db.ClosingLines.Any(c => c.GameId == s.GameId))
            .ExecuteDeleteAsync(ct);

        if (stale > 0)
            logger.LogInformation("Dropped {Count} odds scans from before the {Floor} floor",
                stale, _settings.HistoryFloor);

        return stale + settled;
    }

    /// <summary>
    /// Drops odds partitions that no longer hold anything.
    ///
    /// Pruning by <c>DELETE</c> leaves the pages behind until a vacuum reclaims them; a
    /// partition that is now empty can be dropped outright, which returns the space
    /// immediately and is a metadata operation rather than a scan. The current and next
    /// months are never touched, nor is the default partition, which must always exist for a
    /// row to have somewhere to land.
    /// </summary>
    public async Task<int> DropEmptyPartitionsAsync(CancellationToken ct = default)
    {
        // Nothing in the SQL below may contain a brace. ExecuteSqlRawAsync runs the string
        // through string.Format to bind parameters, so a regex written as [0-9] followed by a
        // braced repeat count reads as parameter placeholders — and with no parameters supplied
        // the statement throws before Postgres ever sees it. That is why the partition-name
        // pattern is spelled out one character class at a time. This comment lives out here for
        // the same reason: written inside the literal, it would reintroduce the problem it
        // describes.
        var dropped = await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE
                part record;
                keep_from date := date_trunc('month', now())::date;
                n bigint;
            BEGIN
                FOR part IN
                    SELECT c.relname AS name
                    FROM pg_inherits i
                    JOIN pg_class c ON c.oid = i.inhrelid
                    JOIN pg_class p ON p.oid = i.inhparent
                    WHERE p.relname = 'OddsSnapshots'
                      AND c.relname ~ '^OddsSnapshots_[0-9][0-9][0-9][0-9]_[0-9][0-9]$'
                LOOP
                    -- Only months strictly before the current one are candidates.
                    CONTINUE WHEN to_date(right(part.name, 7), 'YYYY_MM') >= keep_from;

                    EXECUTE format('SELECT count(*) FROM %I', part.name) INTO n;
                    CONTINUE WHEN n > 0;

                    EXECUTE format('DROP TABLE %I', part.name);
                    RAISE NOTICE 'dropped empty odds partition %', part.name;
                END LOOP;
            END $$;
            """, ct);

        return dropped;
    }
}

public record RetentionReport(int Promoted, int Pruned);
