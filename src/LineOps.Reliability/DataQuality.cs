using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Reliability;

/// <summary>One sport's count of games in a condition, and the earliest of them.</summary>
public record DataQualityCount(string SportKey, int Games, DateTimeOffset Earliest);

/// <summary>
/// One season part and how much of it the data accounts for.
/// </summary>
/// <param name="Expected">What the league schedules, where it fixes a number (<see cref="SeasonCalendar.ExpectedGames"/>).</param>
/// <param name="Held">Games on record, whatever their state.</param>
/// <param name="WithMarketClose">Finals closed by a book market rather than the stats provider's single-book reference.</param>
/// <param name="WithClose">Finals with any close, market or reference.</param>
public record SeasonCoverage(
    string SportKey,
    int SeasonYear,
    SeasonType SeasonType,
    int? Expected,
    int Held,
    int Final,
    int Postponed,
    int WithStats,
    int WithMarketClose,
    int WithClose)
{
    /// <summary>Scheduled games the record does not hold at all, where the schedule is known.</summary>
    public int? Missing => Expected is { } expected ? Math.Max(0, expected - Held) : null;

    /// <summary>Finals the record holds but cannot yet use for a player's line.</summary>
    public int FinalsWithoutStats => Final - WithStats;
}

/// <summary>
/// What is wrong with the data, as opposed to with a source.
///
/// <para>
/// Every other rule watches a feed: did it run, did it succeed, did it return the usual
/// volume. A feed can pass all three and the data still be wrong — the September 2026
/// outage left three games at Live for a week and a day of finals without box scores, and
/// every source rule was green once the host came back. Those holes were found by hand, with
/// the coverage queries in the seasons research. These are those queries, promoted from a
/// checklist to a rule.
/// </para>
///
/// <para>
/// Bounded to the enabled sports and to a lookback window. A hole older than the window is the
/// backfill's to fill, and a standing alert about last season would be furniture.
/// </para>
/// </summary>
public class DataQuality(LineOpsDbContext db)
{
    /// <summary>Games that started long enough ago to be over and are neither final nor postponed.</summary>
    public Task<IReadOnlyList<DataQualityCount>> UnfinishedAsync(
        TimeSpan stuckAfter, TimeSpan lookback, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return CountAsync(InWindow(now - lookback, now - stuckAfter)
            .Where(g => g.Status != GameStatus.Final && g.Status != GameStatus.Postponed), ct);
    }

    /// <summary>Final games with no player stat lines — a box score that never arrived.</summary>
    public Task<IReadOnlyList<DataQualityCount>> FinalsWithoutStatsAsync(
        TimeSpan grace, TimeSpan lookback, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return CountAsync(InWindow(now - lookback, now - grace)
            .Where(g => g.Status == GameStatus.Final
                        && !db.PlayerGameStats.Any(s => s.GameId == g.Id)), ct);
    }

    /// <summary>
    /// Final games with no closing line from any source. Informational: ESPN occasionally
    /// publishes no close for a game, and where it has none nothing fabricates one.
    /// </summary>
    public Task<IReadOnlyList<DataQualityCount>> FinalsWithoutCloseAsync(
        TimeSpan grace, TimeSpan lookback, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return CountAsync(InWindow(now - lookback, now - grace)
            .Where(g => g.Status == GameStatus.Final
                        && !db.ClosingLines.Any(c => c.GameId == g.Id)), ct);
    }

    private IQueryable<Game> InWindow(DateTimeOffset from, DateTimeOffset to)
        => db.Games.AsNoTracking()
            .Where(g => g.Sport!.Enabled && g.StartsAt >= from && g.StartsAt <= to);

    private async Task<IReadOnlyList<DataQualityCount>> CountAsync(
        IQueryable<Game> games, CancellationToken ct)
    {
        var rows = await games
            .GroupBy(g => g.SportId)
            .Select(x => new { SportId = x.Key, Games = x.Count(), Earliest = x.Min(g => g.StartsAt) })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(r => r.SportId).ToList();
        var keys = await db.Sports.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Key, ct);

        return rows
            .Select(r => new DataQualityCount(keys[r.SportId], r.Games, r.Earliest))
            .OrderBy(c => c.SportKey)
            .ToList();
    }

    /// <summary>
    /// Every season the enabled leagues hold, and how much of it is accounted for.
    ///
    /// <para>
    /// The alert rules above look at a recent window; this is the whole record, one row per
    /// season part, so "we have the 2025 NFL season" is a number rather than a belief — the
    /// report the seasons research asked for in the History window (its §4.6).
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SeasonCoverage>> SeasonsAsync(CancellationToken ct = default)
    {
        var rows = await db.Games.AsNoTracking()
            .Where(g => g.Sport!.Enabled)
            .GroupBy(g => new { g.Sport!.Key, g.SeasonYear, g.SeasonType })
            .Select(x => new
            {
                x.Key.Key,
                x.Key.SeasonYear,
                x.Key.SeasonType,
                Held = x.Count(),
                Final = x.Count(g => g.Status == GameStatus.Final),
                Postponed = x.Count(g => g.Status == GameStatus.Postponed),
                WithStats = x.Count(g => g.Status == GameStatus.Final
                                         && db.PlayerGameStats.Any(s => s.GameId == g.Id)),
                WithMarketClose = x.Count(g => g.Status == GameStatus.Final
                                               && db.ClosingLines.Any(c => c.GameId == g.Id
                                                                           && c.Source!.Kind == SourceKind.Odds)),
                WithClose = x.Count(g => g.Status == GameStatus.Final
                                         && db.ClosingLines.Any(c => c.GameId == g.Id)),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new SeasonCoverage(
                r.Key, r.SeasonYear, r.SeasonType,
                SeasonCalendar.ExpectedGames(r.Key, r.SeasonYear, r.SeasonType),
                r.Held, r.Final, r.Postponed, r.WithStats, r.WithMarketClose, r.WithClose))
            .OrderBy(s => s.SportKey)
            .ThenByDescending(s => s.SeasonYear)
            .ThenBy(s => s.SeasonType)
            .ToList();
    }

    /// <summary>"MLB 10 since 4 Sep, NFL 2 since 14 Sep" — the numbers an operator acts on.</summary>
    public static string Describe(IReadOnlyList<DataQualityCount> counts)
        => string.Join(", ", counts.Select(c =>
            $"{c.SportKey.ToUpperInvariant()} {c.Games} since {LeagueClock.DateOf(c.Earliest):d MMM}"));
}
