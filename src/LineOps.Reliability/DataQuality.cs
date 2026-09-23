using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Reliability;

/// <summary>One sport's count of games in a condition, and the earliest of them.</summary>
public record DataQualityCount(string SportKey, int Games, DateTimeOffset Earliest);

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

    /// <summary>"MLB 10 since 4 Sep, NFL 2 since 14 Sep" — the numbers an operator acts on.</summary>
    public static string Describe(IReadOnlyList<DataQualityCount> counts)
        => string.Join(", ", counts.Select(c =>
            $"{c.SportKey.ToUpperInvariant()} {c.Games} since {LeagueClock.DateOf(c.Earliest):d MMM}"));
}
