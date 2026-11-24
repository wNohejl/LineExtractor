using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Ingestion.Services;
using XLines.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Adapters;

/// <summary>
/// The two zeroes of ADR 0003 a feed that lists only games yet to start can now give: an empty
/// calendar is a normal, free answer; games the provider cannot price are still the signature of
/// a silent upstream break.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OddsIngestionStatusTests(PostgresFixture fixture)
{
    private sealed class StubOdds(OddsFetchResult result, string key) : IOddsSource
    {
        public string Key => key;
        public IReadOnlyList<string> SupportedMarkets => Markets.V1;
        public Task<OddsFetchResult> FetchSlateAsync(string s, IReadOnlyList<string> m, CancellationToken ct) => Task.FromResult(result);
    }

    /// <summary>A provider that answered, billed the call, and sent a payload none of which could be read.</summary>
    private sealed class DriftedOdds(FetchCost cost, string key) : IOddsSource
    {
        public string Key => key;
        public IReadOnlyList<string> SupportedMarkets => Markets.V1;
        public Task<OddsFetchResult> FetchSlateAsync(string s, IReadOnlyList<string> m, CancellationToken ct)
            => throw new OddsFetchFailedException("mlb listed 2 events and none could be read — possible upstream schema drift.", cost);
    }

    private async Task<IngestionOutcome> RunAsync(OddsFetchResult result)
        => (await RunAsync(key => new StubOdds(result, key))).Outcome;

    private async Task<(IngestionOutcome Outcome, IngestionRun Run)> RunAsync(Func<string, IOddsSource> source)
    {
        await using var db = fixture.CreateContext();
        var key = $"book-{Guid.NewGuid():N}"[..13];

        db.Sources.Add(new Source { Key = key, Name = "Stub book", Kind = SourceKind.Odds, BaseUrl = "local://" });
        await db.SaveChangesAsync();

        var budget = new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance);

        var outcome = await new OddsIngestionService(db, new EntityResolver(db), budget, NullLogger<OddsIngestionService>.Instance)
            .IngestAsync(source(key), "mlb", "test", CancellationToken.None);

        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);

        // The database is shared, and the reliability suites read every source in it: a stub
        // source left holding a run and no success would read there as a source gone stale.
        await db.IngestionRuns.Where(r => r.Source!.Key == key).ExecuteDeleteAsync();
        await db.Sources.Where(s => s.Key == key).ExecuteDeleteAsync();

        return (outcome, run);
    }

    [Fact]
    public async Task A_provider_that_lists_no_games_is_an_empty_slate_not_a_broken_feed()
    {
        var outcome = await RunAsync(new OddsFetchResult([], [], new FetchCost(1, 0)));

        Assert.Equal(RunStatus.Success, outcome.Status);
        Assert.Equal(0, outcome.RowsIngested);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task Games_listed_with_no_prices_at_all_are_still_partial()
    {
        var game = new CanonicalGame($"g-{Guid.NewGuid():N}", "mlb", "Seattle Mariners", "Houston Astros",
            DateTimeOffset.UtcNow.AddHours(3));

        var outcome = await RunAsync(new OddsFetchResult([game], [], new FetchCost(1, 3)));

        Assert.Equal(RunStatus.Partial, outcome.Status);
        Assert.Equal(0, outcome.RowsIngested);
        Assert.Contains("no prices", outcome.Error);
    }

    [Fact]
    public async Task A_payload_none_of_which_could_be_read_fails_the_run_and_records_what_it_was_billed()
    {
        var (outcome, run) = await RunAsync(key => new DriftedOdds(new FetchCost(Requests: 1, Credits: 3, CreditsRemaining: 431), key));

        // Still a failure, never a quiet day: the provider listed games and none could be read.
        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Contains("schema drift", outcome.Error);

        // But the call was billed, so the failed run carries the spend and the provider's remainder,
        // which the monthly count, the budget guard and pacing all read without regard to status.
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(1, run.RequestsMade);
        Assert.Equal(3, run.CreditsSpent);
        Assert.Equal(431, run.ProviderCreditsRemaining);
    }
}
