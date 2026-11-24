using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using Microsoft.EntityFrameworkCore;

namespace XLines.Web.Services;

/// <summary>
/// What the Board row's Form answer reads for a team sport: the game's row (each side's best
/// price, fair price, every book's rung and hold), its stored prices on every market (where each
/// book opened), and each club's games this season of this season type. One read per opened row;
/// nothing here calls a provider. A seam so the answer renders in tests against what a test hands it.
/// </summary>
public interface IMatchupData
{
    Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default);

    /// <summary>Every stored price on the game's markets, oldest first, to the reader's tier.</summary>
    Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, CancellationToken ct = default);

    /// <summary>A club's games in one season and season type, newest first, graded against the close.</summary>
    Task<IReadOnlyList<TeamGameLogRow>> SeasonAsync(int teamId, int seasonYear, SeasonType seasonType, CancellationToken ct = default);
}

/// <summary>The seam over the real services, a scope per read.</summary>
public sealed class MatchupData(IServiceScopeFactory scopes) : IMatchupData
{
    public Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
        => scopes.InScopeAsync<BoardService, BoardRow?>(board => board.GetRowAsync(gameId, ct));

    public async Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();
        var tier = scope.ServiceProvider.GetService<IDataTier>() ?? FullDataTier.Instance;

        // To the reader's tier (ADR 0024), as every read of the scans is.
        return await db.OddsSnapshots.FromVisible(await db.VisibleSourceIdsAsync(tier, ct)).AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamGameLogRow>> SeasonAsync(int teamId, int seasonYear, SeasonType seasonType,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var log = await scope.ServiceProvider.GetRequiredService<GameLogService>()
            .TeamGameLogAsync(teamId, TimeSpan.Zero, seasonYear, ct);

        if (log.Count == 0)
            return log;

        // The season year is the log's own bound; the season type is held to here, so a
        // preseason game is not counted in a regular-season record.
        var ids = log.Select(r => r.GameId).ToList();
        var db = scope.ServiceProvider.GetRequiredService<XLinesDbContext>();
        var ofType = await db.Games.AsNoTracking()
            .Where(g => ids.Contains(g.Id) && g.SeasonType == seasonType)
            .Select(g => g.Id)
            .ToListAsync(ct);

        var keep = ofType.ToHashSet();
        return log.Where(r => keep.Contains(r.GameId)).ToList();
    }
}
