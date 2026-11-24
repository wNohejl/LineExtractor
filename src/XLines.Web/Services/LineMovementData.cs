using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace XLines.Web.Services;

/// <summary>
/// What the Line movement window reads, as one seam: the games with moves on record, one game,
/// one market's stored prices, the line polling setting, and the rosters' form. Every read of the
/// odds tier is to the reader's tier (ADR 0024). A seam rather than queries in the panel so the
/// window can be rendered against what a test hands it, as the wager windows are (IWagerData).
/// </summary>
public interface ILineMovementData
{
    /// <summary>
    /// Games this reader has moves to see, the ones nearest now: those ahead, soonest first, then
    /// those played, latest first (<see cref="LineMovementData.Nearest"/>).
    /// </summary>
    Task<IReadOnlyList<Game>> GamesAsync(CancellationToken ct = default);

    /// <summary>One game with its teams and sport, or null.</summary>
    Task<Game?> GameAsync(int gameId, CancellationToken ct = default);

    /// <summary>Every stored price on one market of one game, both sides, oldest first.</summary>
    Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, string market, CancellationToken ct = default);

    /// <summary>Whether lines are scanned on a schedule; false is Manual, the desk's default.</summary>
    Task<bool> ScheduledAsync(CancellationToken ct = default);

    /// <summary>Both rosters' recent form, read from the stats history by reference.</summary>
    Task<MatchupForm?> FormAsync(int gameId, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// The slate from the start of today to two days out: its biggest moves today, and its next
    /// games by start time (Line watch, before anything is pressed).
    /// </summary>
    Task<SlateOverview> SlateAsync(CancellationToken ct = default);
}

/// <summary>The seam over the database, a context per read as the panel's own reads were.</summary>
public sealed class LineMovementData(
    IDbContextFactory<XLinesDbContext> dbFactory,
    IServiceScopeFactory scopes,
    IDataTier tier,
    IOptions<IngestionOptions> ingestion) : ILineMovementData
{
    public async Task<IReadOnlyList<Game>> GamesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Only games this reader has moves to see (ADR 0024): an evaluation feed's are the operator's.
        var scans = db.OddsSnapshots.FromVisible(await db.VisibleSourceIdsAsync(tier, ct));

        var games = db.Games.AsNoTracking()
            .Include(g => g.HomeTeam).Include(g => g.AwayTeam)
            .Where(g => scans.Any(s => s.GameId == g.Id));

        // Two reads, each to the cap, then one list: ordered by start alone and cut at the cap, a
        // desk holding a season's closing lines listed last April's games and lost the slate ahead.
        var now = DateTimeOffset.UtcNow;
        var ahead = await games.Where(g => g.StartsAt >= now).OrderBy(g => g.StartsAt).Take(Cap).ToListAsync(ct);
        var played = await games.Where(g => g.StartsAt < now).OrderByDescending(g => g.StartsAt).Take(Cap).ToListAsync(ct);

        return Nearest([.. ahead, .. played], now, Cap);
    }

    /// <summary>How many games the window's picker lists.</summary>
    public const int Cap = 120;

    /// <summary>
    /// The games nearest now, at most <paramref name="cap"/>: every one ahead first, soonest
    /// first, and the room left to the ones played, latest first. A game aimed at from elsewhere
    /// and not among them is still shown — the window adds it to its picker.
    /// </summary>
    public static IReadOnlyList<Game> Nearest(IEnumerable<Game> games, DateTimeOffset now, int cap)
    {
        var all = games.ToList();

        return all.Where(g => g.StartsAt >= now).OrderBy(g => g.StartsAt)
            .Concat(all.Where(g => g.StartsAt < now).OrderByDescending(g => g.StartsAt))
            .Take(cap)
            .ToList();
    }

    public async Task<Game?> GameAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Games.AsNoTracking()
            .Include(g => g.HomeTeam).Include(g => g.AwayTeam).Include(g => g.Sport)
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);
    }

    public async Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, string market, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.OddsSnapshots.FromVisible(await db.VisibleSourceIdsAsync(tier, ct)).AsNoTracking()
            .Where(s => s.GameId == gameId && s.Market == market)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> ScheduledAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // The operator's setting, as Ops sets it; configuration's answer where none is stored.
        return await db.LinesRunUnattendedAsync(ingestion.Value.LinePolling.RunsUnattended, ct);
    }

    public Task<MatchupForm?> FormAsync(int gameId, TimeSpan window, CancellationToken ct = default)
        => scopes.InScopeAsync<MatchupCrossReference, MatchupForm?>(lookup => lookup.GetAsync(gameId, window, ct: ct));

    public async Task<SlateOverview> SlateAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var today = XLines.Core.Analytics.LeagueClock.StartOf(XLines.Core.Analytics.LeagueClock.Today());
        var until = now.AddDays(2);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var games = await db.Games.AsNoTracking()
            .Include(g => g.HomeTeam).Include(g => g.AwayTeam).Include(g => g.Sport)
            .Where(g => g.StartsAt >= today && g.StartsAt <= until)
            .OrderBy(g => g.StartsAt)
            .ToListAsync(ct);

        if (games.Count == 0)
            return SlateOverview.Empty;

        var ids = games.Select(g => g.Id).ToList();

        // To the reader's tier (ADR 0024), as every read of the scans is; the three markets the
        // overview speaks for. A move is where a book opened and where it is now, so the database
        // hands back only each book's first and last capture on a side, and only for sides whose
        // last capture landed today and that were written more than once — not every scan of two
        // days of games as full rows on every load.
        var scans = db.OddsSnapshots.FromVisible(await db.VisibleSourceIdsAsync(tier, ct)).AsNoTracking()
            .Where(s => ids.Contains(s.GameId)
                        && (s.Market == Markets.Moneyline || s.Market == Markets.Spread || s.Market == Markets.Total));

        var ends = await scans
            .GroupBy(s => new { s.GameId, s.Market, s.Outcome, s.Book })
            .Select(g => new { g.Key.GameId, g.Key.Market, g.Key.Outcome, g.Key.Book, First = g.Min(s => s.CapturedAt), Last = g.Max(s => s.CapturedAt) })
            .Where(g => g.Last >= today && g.First < g.Last)
            .ToListAsync(ct);

        if (ends.Count == 0)
            return new SlateOverview([], games.Where(g => g.Status == GameStatus.Scheduled && g.StartsAt >= now).Take(6).ToList());

        var moved = ends.Select(e => e.GameId).Distinct().ToList();
        var times = ends.SelectMany(e => new[] { e.First, e.Last }).Distinct().ToList();
        var wanted = ends
            .SelectMany(e => new[] { (e.GameId, e.Market, e.Outcome, e.Book, e.First), (e.GameId, e.Market, e.Outcome, e.Book, e.Last) })
            .ToHashSet();

        var rows = await scans
            .Where(s => moved.Contains(s.GameId) && times.Contains(s.CapturedAt))
            .Select(s => new { s.GameId, s.Market, s.Outcome, s.Book, s.Line, s.PriceAmerican, s.CapturedAt })
            .ToListAsync(ct);

        var snapshots = rows
            .Where(r => wanted.Contains((r.GameId, r.Market, r.Outcome, r.Book, r.CapturedAt)))
            .Select(r => new OddsSnapshot
            {
                GameId = r.GameId, Market = r.Market, Outcome = r.Outcome, Book = r.Book,
                Line = r.Line, PriceAmerican = r.PriceAmerican, CapturedAt = r.CapturedAt
            })
            .OrderBy(s => s.CapturedAt)
            .ToList();

        var next = games.Where(g => g.Status == GameStatus.Scheduled && g.StartsAt >= now).Take(6).ToList();

        return new SlateOverview(SlateMoves.Biggest(snapshots, games, today), next);
    }
}
