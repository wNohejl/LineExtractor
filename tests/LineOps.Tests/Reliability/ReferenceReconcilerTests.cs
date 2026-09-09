using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The enabled flags on sports and sources belong to the configuration, not to a hand edit.
///
/// <para>
/// A provider taken out of configuration used to keep its source row enabled: it stayed in
/// the SLO count, was alerted on for never running, and those alerts were promoted into
/// incidents about a feed nobody had asked for. Two leagues out of scope were switched off by
/// hand in one database and travelled in the snapshot, so a fresh volume seeded all four.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class ReferenceReconcilerTests(PostgresFixture fixture)
{
    /// <summary>An odds source that exists only to be registered.</summary>
    private sealed class StubOdds(string key) : IOddsSource
    {
        public string Key => key;
        public IReadOnlyList<string> SupportedMarkets => [];

        public Task<OddsFetchResult> FetchSlateAsync(string sportKey, IReadOnlyList<string> markets, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private ReferenceReconciler Create(IngestionOptions options, params string[] registeredOddsKeys)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext());
        services.AddScoped(_ => new SourceRegistry(registeredOddsKeys.Select(k => new StubOdds(k)), []));

        return new ReferenceReconciler(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReferenceReconciler>.Instance);
    }

    [Fact]
    public async Task A_source_that_is_not_configured_is_disabled_and_its_open_alerts_are_answered()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var live = new Source { Key = $"live-{suffix}", Name = "live", Kind = SourceKind.Odds, Enabled = true };
        var gone = new Source { Key = $"gone-{suffix}", Name = "gone", Kind = SourceKind.Odds, Enabled = true };
        db.Sources.AddRange(live, gone);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        // The incident nobody wrote up goes; the one with a root cause is a record and stays.
        var unwritten = new Incident { Title = "gone never ran", Severity = AlertSeverity.Critical, OpenedAt = now };
        var written = new Incident { Title = "gone failed", Severity = AlertSeverity.Warn, OpenedAt = now, RootCause = "key revoked" };
        db.Incidents.AddRange(unwritten, written);
        await db.SaveChangesAsync();

        db.Alerts.AddRange(
            new Alert { RuleKey = "freshness", SourceId = gone.Id, Severity = AlertSeverity.Critical, Message = "never", TriggeredAt = now, IncidentId = unwritten.Id },
            new Alert { RuleKey = "success_rate", SourceId = gone.Id, Severity = AlertSeverity.Warn, Message = "0%", TriggeredAt = now, IncidentId = written.Id },
            new Alert { RuleKey = "freshness", SourceId = live.Id, Severity = AlertSeverity.Warn, Message = "stale", TriggeredAt = now });
        await db.SaveChangesAsync();

        await Create(new IngestionOptions(), live.Key).StartAsync(CancellationToken.None);

        await using var check = fixture.CreateContext();

        Assert.True((await check.Sources.SingleAsync(s => s.Id == live.Id)).Enabled);
        Assert.False((await check.Sources.SingleAsync(s => s.Id == gone.Id)).Enabled);

        var alerts = await check.Alerts.Where(a => a.SourceId == gone.Id || a.SourceId == live.Id).ToListAsync();

        Assert.All(alerts.Where(a => a.SourceId == gone.Id), a => Assert.NotNull(a.ResolvedAt));
        Assert.Null(alerts.Single(a => a.SourceId == live.Id).ResolvedAt);

        Assert.Null(await check.Incidents.FirstOrDefaultAsync(i => i.Id == unwritten.Id));
        Assert.NotNull(await check.Incidents.FirstOrDefaultAsync(i => i.Id == written.Id));
    }

    [Fact]
    public async Task A_sport_is_enabled_exactly_when_configuration_names_it()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var inScope = new Sport { Key = $"in-{suffix}", Name = "IN", Enabled = false };
        var outOfScope = new Sport { Key = $"out-{suffix}", Name = "OUT", Enabled = true };
        db.Sports.AddRange(inScope, outOfScope);
        await db.SaveChangesAsync();

        await Create(new IngestionOptions { Sports = [inScope.Key] }).StartAsync(CancellationToken.None);

        await using var check = fixture.CreateContext();

        Assert.True((await check.Sports.SingleAsync(s => s.Id == inScope.Id)).Enabled);
        Assert.False((await check.Sports.SingleAsync(s => s.Id == outOfScope.Id)).Enabled);
    }
}
