using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Services;
using XLines.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Tests.Reliability;

/// <summary>
/// A stats pass that was answered, and billed, but failed records what it spent, as the odds pass
/// does: the budget guard, the month's count and pacing read the runs, and a failed run that
/// recorded nothing left the month reading low until the next good call.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StatsFailedRunCostTests(PostgresFixture fixture)
{
    private sealed class StubStats(string key, Func<StatsFetchResult> answer) : IStatsSource
    {
        public string Key => key;
        public Task<StatsFetchResult> FetchScheduleAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(answer());
        public Task<StatsFetchResult> FetchRosterAsync(string s, CancellationToken ct) => Task.FromResult(answer());
        public Task<StatsFetchResult> FetchBoxScoresAsync(string s, DateOnly d, CancellationToken ct) => Task.FromResult(answer());
    }

    private static async Task<string> SeedAsync(XLinesDbContext db)
    {
        if (!await db.Sports.AnyAsync(s => s.Key == "nfl"))
            db.Sports.Add(new Sport { Key = "nfl", Name = "NFL" });

        var key = $"stats-{Guid.NewGuid():N}"[..20];
        db.Sources.Add(new Source { Key = key, Name = "Stub stats", Kind = SourceKind.Stats, BaseUrl = "local://" });
        await db.SaveChangesAsync();
        return key;
    }

    private static StatsIngestionService Service(XLinesDbContext db)
        => new(db, new EntityResolver(db),
            new CreditBudgetGuard(new BudgetCalculator(db), NullLogger<CreditBudgetGuard>.Instance),
            NullLogger<StatsIngestionService>.Instance);

    /// <summary>
    /// Removes the stub source and its runs. A source whose only runs failed reads as stale, and the
    /// alert engine evaluates every source in the shared database, so one left behind would raise a
    /// freshness alert in another test's evaluation.
    /// </summary>
    private async Task ForgetAsync(string key)
    {
        await using var db = fixture.CreateContext();
        var id = await db.Sources.Where(s => s.Key == key).Select(s => s.Id).SingleAsync();
        await db.IngestionRuns.Where(r => r.SourceId == id).ExecuteDeleteAsync();
        await db.Sources.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>One pass of the service against a fresh stub source, read back from a clean context.</summary>
    private async Task<IngestionRun> FailedRunAsync(Func<StatsFetchResult> answer)
    {
        string key;
        IngestionOutcome outcome;

        await using (var db = fixture.CreateContext())
        {
            key = await SeedAsync(db);

            try
            {
                outcome = await Service(db).IngestAsync(
                    new StubStats(key, answer), "nfl", "test", DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
            }
            catch
            {
                await ForgetAsync(key);
                throw;
            }
        }

        try
        {
            Assert.Equal(RunStatus.Failed, outcome.Status);

            await using var read = fixture.CreateContext();
            return await read.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        }
        finally
        {
            await ForgetAsync(key);
        }
    }

    [Fact]
    public async Task AFetchThatAnsweredAndThenFailedToSaveRecordsItsCostOnTheFailedRun()
    {
        // A side longer than the team name's column fails the save after the provider has answered.
        var tag = Guid.NewGuid().ToString("N");
        var result = new StatsFetchResult(
            [new CanonicalGame($"g-{tag}", "nfl", new string('x', 300), $"Away {tag}", DateTimeOffset.UtcNow.AddDays(1))],
            [], [], new FetchCost(1, 2, 480));

        var run = await FailedRunAsync(() => result);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((1, 2, (int?)480), (run.RequestsMade, run.CreditsSpent, run.ProviderCreditsRemaining));
    }

    [Fact]
    public async Task AFetchThatThrowsWithItsCostRecordsItOnTheFailedRun()
    {
        var run = await FailedRunAsync(() => throw new OddsFetchFailedException("unreadable", new FetchCost(1, 2, 476)));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("OddsFetchFailedException: unreadable", run.Error);
        Assert.Equal((1, 2, (int?)476), (run.RequestsMade, run.CreditsSpent, run.ProviderCreditsRemaining));
    }

    [Fact]
    public async Task AFetchThatThrowsWithNoCostRecordsNone()
    {
        var run = await FailedRunAsync(() => throw new HttpRequestException("timed out"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((0, 0, (int?)null), (run.RequestsMade, run.CreditsSpent, run.ProviderCreditsRemaining));
    }
}
