using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// What the windows that log wagers read and write, in one seam: a board row, the journal's last
/// bet on a side, the writes, and the lines pull a stale price offers. An interface so the wager
/// window and the slip can be rendered in tests without a database.
/// </summary>
public interface IWagerData
{
    Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default);

    Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome,
        DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Straight bets, all in one save (<see cref="JournalService.SaveEntriesAsync"/>).</summary>
    Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default);

    Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default);

    /// <summary>The lines pull for one league (<c>odds:lines:{sport}</c>) as the pull menu describes it.</summary>
    Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default);

    Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default);

    /// <summary>Games to log a bet against, by team: today first, then back through the season, then the games to come.</summary>
    Task<IReadOnlyList<Game>> SearchGamesAsync(string text, CancellationToken ct = default);

    /// <summary>Every book the desk knows of — scanned, journalled, or common — for a typed price.</summary>
    Task<IReadOnlyList<string>> KnownBooksAsync(CancellationToken ct = default);

}

/// <summary>
/// The seam over the real services. A scope per call, as the panels did before it, so a window
/// never holds a DbContext across renders. Every write goes through <see cref="JournalService"/>,
/// whose saves the change notifier announces to every open window.
/// </summary>
public sealed class WagerData(IServiceScopeFactory scopes, IngestionJobs jobs) : IWagerData
{
    public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().GetRowAsync(gameId, ct);
    }

    public async Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome,
        DateTimeOffset since, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JournalService>()
            .LatestOnSideAsync(gameId, market, outcome, since, ct);
    }

    public async Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JournalService>().SaveEntriesAsync(bets, ct);
    }

    public async Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JournalService>().SaveParlayAsync(parlay, ct);
    }

    public async Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default)
        => (await jobs.DescribeAsync(ct)).FirstOrDefault(j => j.Key == IngestionJobs.OddsLinesPrefix + sportKey);

    public Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default) => jobs.RunAsync(key, ct);

    public async Task<IReadOnlyList<Game>> SearchGamesAsync(string text, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JournalService>()
            .SearchGamesAsync(text, take: 30, fromToday: true, ct: ct);
    }

    public async Task<IReadOnlyList<string>> KnownBooksAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JournalService>().KnownBooksAsync(ct);
    }
}
