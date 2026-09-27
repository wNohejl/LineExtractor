using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;
using LineOps.Web.Services;

namespace LineOps.Web.Tests.Support;

/// <summary>The data seam, in memory: a row to price, a journal reading, and a record of what was written.</summary>
public sealed class FakeWagerData : IWagerData
{
    public BoardRow? Row { get; set; } = BoardRows.Row();

    /// <summary>Set to hold <see cref="RowAsync"/> open, so a test can see the loading state.</summary>
    public TaskCompletionSource? RowGate { get; set; }

    public int RowReads { get; private set; }
    public JournalEntry? Latest { get; set; }
    public int LatestReads { get; private set; }
    public IngestionJob? LinesJob { get; set; }
    public List<string> JobsRun { get; } = [];
    public List<EntryDraft> Straights { get; } = [];
    public List<ParlayDraft> Parlays { get; } = [];

    /// <summary>Set to make the next writes fail with this.</summary>
    public Exception? FailWith { get; set; }

    public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        RowReads++;

        if (RowGate is { } gate)
            await gate.Task;

        return Row;
    }

    public Task<JournalEntry?> LatestOnSideAsync(int gameId, string market, string outcome, DateTimeOffset since, CancellationToken ct = default)
    {
        LatestReads++;
        return Task.FromResult(Latest);
    }

    public Task LogStraightsAsync(IReadOnlyList<EntryDraft> bets, CancellationToken ct = default)
    {
        if (FailWith is { } ex)
            return Task.FromException(ex);

        Straights.AddRange(bets);
        return Task.CompletedTask;
    }

    public Task LogParlayAsync(ParlayDraft parlay, CancellationToken ct = default)
    {
        if (FailWith is { } ex)
            return Task.FromException(ex);

        Parlays.Add(parlay);
        return Task.CompletedTask;
    }

    public Task<IngestionJob?> LinesJobAsync(string sportKey, CancellationToken ct = default)
        => Task.FromResult(LinesJob);

    public Task<JobOutcome> RunJobAsync(string key, CancellationToken ct = default)
    {
        JobsRun.Add(key);
        return Task.FromResult(new JobOutcome(key, true, 12, 0, null));
    }
}
