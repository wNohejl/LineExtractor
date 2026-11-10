using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The league's injury report written onto its players: matched by the provider's id, by name
/// where the id is not on file, never creating a player, and leaving alone a player the report
/// no longer names. One run row for the pass.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InjuryReportTests(PostgresFixture fixture)
{
    private sealed class StubReport(string key) : IInjurySource
    {
        public string Key => key;

        public List<CanonicalInjury> Report { get; set; } = [];

        public bool Fails { get; set; }

        public Task<IReadOnlyList<CanonicalInjury>> FetchInjuriesAsync(string sportKey, CancellationToken ct)
            => Fails
                ? throw new HttpRequestException("503 (Service Unavailable)")
                : Task.FromResult<IReadOnlyList<CanonicalInjury>>(Report);
    }

    /// <summary>A private league and source per test, so clearing one report touches no other test's players.</summary>
    private static async Task<(Sport Sport, Team Vikings, StubReport Source)> SeedAsync(LineOpsDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var sport = db.Sports.Add(new Sport { Key = $"inj-{tag}", Name = "Injury league" }).Entity;
        var source = new StubReport($"inj-{tag}");
        db.Sources.Add(new Source { Key = source.Key, Name = "Stub report", Kind = SourceKind.Stats, BaseUrl = "local://stub" });
        await db.SaveChangesAsync();

        var vikings = db.Teams.Add(new Team { SportId = sport.Id, Name = "Minnesota Vikings", Abbrev = "MIN" }).Entity;
        await db.SaveChangesAsync();

        return (sport, vikings, source);
    }

    private static Player Player(Sport sport, Team team, string name, string? sourceKey = null, string? id = null)
        => new()
        {
            SportId = sport.Id,
            TeamId = team.Id,
            FullName = name,
            Position = "QB",
            ExternalIds = sourceKey is null ? [] : new Dictionary<string, string> { [sourceKey] = id! }
        };

    private static CanonicalInjury Entry(string id, string name, string status, string? note = null)
        => new(id, name, "QB", "16", "Minnesota Vikings", status, note);

    private static InjuryReportService Service(LineOpsDbContext db, StubReport source)
        => new(db, [source], NullLogger<InjuryReportService>.Instance);

    [Fact]
    public async Task The_report_is_written_onto_the_players_it_names()
    {
        await using var db = fixture.CreateContext();
        var (sport, vikings, source) = await SeedAsync(db);
        var murray = Player(sport, vikings, "Kyler Murray", source.Key, "3917315");
        // Known to the desk by name only — another source made him.
        var wentz = Player(sport, vikings, "Carson Wentz");
        db.Players.AddRange(murray, wentz);
        await db.SaveChangesAsync();

        source.Report =
        [
            Entry("3917315", "Kyler Murray", "Out", "Murray (hamstring) has been ruled out for Monday's game versus the Bills."),
            Entry("2573079", "carson wentz", "Active"),
            Entry("9999999", "Nobody On File", "Questionable")
        ];

        var before = DateTimeOffset.UtcNow;
        var outcome = await Service(db, source).RefreshAsync(sport.Key, InjuryReportService.JobKey, CancellationToken.None);

        Assert.Equal(RunStatus.Success, outcome.Status);
        Assert.Equal(2, outcome.RowsIngested);

        var players = await db.Players.AsNoTracking().Where(p => p.SportId == sport.Id).ToDictionaryAsync(p => p.FullName);
        Assert.Equal("Out", players["Kyler Murray"].InjuryStatus);
        Assert.Equal("Murray (hamstring) has been ruled out for Monday's game versus the Bills.", players["Kyler Murray"].InjuryNote);
        Assert.True(players["Kyler Murray"].InjuryUpdatedAt >= before);
        Assert.Equal("Active", players["Carson Wentz"].InjuryStatus);
        Assert.Null(players["Carson Wentz"].InjuryNote);
        // Matched by name alone, he now carries the report's id, so the next report finds him exactly.
        Assert.Equal("2573079", players["Carson Wentz"].ExternalIds[source.Key]);
        Assert.False(players.ContainsKey("Nobody On File"));

        var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId);
        Assert.Equal("espn:injuries", run.JobKey);
        Assert.Equal(1, run.RequestsMade);
        Assert.Equal(2, run.RowsIngested);
        Assert.Equal("3 on the report, 2 matched, 1 not on file.", run.Detail);
    }

    /// <summary>
    /// ESPN lists twenty-five entries a club, newest first, so a player ruled out weeks ago drops
    /// off the bottom as others are added. His absence is a full list, not a recovery: he keeps
    /// his status, note and time, and the time is what ages it (<see cref="LineOps.Core.Entities.Player.InjuryStatusLife"/>).
    /// </summary>
    [Fact]
    public async Task A_player_the_report_no_longer_names_is_left_as_he_was()
    {
        await using var db = fixture.CreateContext();
        var (sport, vikings, source) = await SeedAsync(db);
        var murray = Player(sport, vikings, "Kyler Murray", source.Key, "3917315");
        var wentz = Player(sport, vikings, "Carson Wentz", source.Key, "2573079");
        var ruledOut = new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(-20).UtcDateTime.Date, TimeSpan.Zero);
        wentz.InjuryStatus = "Out";
        wentz.InjuryNote = "Wentz (shoulder) is out.";
        wentz.InjuryUpdatedAt = ruledOut;
        db.Players.AddRange(murray, wentz);
        await db.SaveChangesAsync();

        source.Report = [Entry("3917315", "Kyler Murray", "Out")];

        var outcome = await Service(db, source).RefreshAsync(sport.Key, InjuryReportService.JobKey, CancellationToken.None);

        Assert.Equal(1, outcome.RowsIngested);

        var after = await db.Players.AsNoTracking().SingleAsync(p => p.Id == wentz.Id);
        Assert.Equal("Out", after.InjuryStatus);
        Assert.Equal("Wentz (shoulder) is out.", after.InjuryNote);
        Assert.Equal(ruledOut, after.InjuryUpdatedAt);
    }

    /// <summary>An empty report is a feed that said nothing, not a league where everybody healed.</summary>
    [Fact]
    public async Task An_empty_report_clears_nobody()
    {
        await using var db = fixture.CreateContext();
        var (sport, vikings, source) = await SeedAsync(db);
        var murray = Player(sport, vikings, "Kyler Murray", source.Key, "3917315");
        murray.InjuryStatus = "Out";
        db.Players.Add(murray);
        await db.SaveChangesAsync();

        var outcome = await Service(db, source).RefreshAsync(sport.Key, InjuryReportService.JobKey, CancellationToken.None);

        Assert.Equal(0, outcome.RowsIngested);
        Assert.Equal("Out", (await db.Players.AsNoTracking().SingleAsync(p => p.Id == murray.Id)).InjuryStatus);
    }

    [Fact]
    public async Task A_report_that_will_not_load_is_a_failed_run_and_changes_nothing()
    {
        await using var db = fixture.CreateContext();
        var (sport, vikings, source) = await SeedAsync(db);
        var murray = Player(sport, vikings, "Kyler Murray", source.Key, "3917315");
        murray.InjuryStatus = "Out";
        db.Players.Add(murray);
        await db.SaveChangesAsync();
        source.Fails = true;

        var outcome = await Service(db, source).RefreshAsync(sport.Key, InjuryReportService.JobKey, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Contains("503", outcome.Error);
        Assert.Equal("Out", (await db.Players.AsNoTracking().SingleAsync(p => p.Id == murray.Id)).InjuryStatus);
        Assert.Equal(RunStatus.Failed, (await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.Id == outcome.RunId)).Status);
    }

    [Fact]
    public async Task A_name_shared_by_two_players_is_settled_by_the_team()
    {
        await using var db = fixture.CreateContext();
        var (sport, vikings, source) = await SeedAsync(db);
        var bills = db.Teams.Add(new Team { SportId = sport.Id, Name = "Buffalo Bills", Abbrev = "BUF" }).Entity;
        await db.SaveChangesAsync();
        var ours = Player(sport, vikings, "Josh Allen");
        var theirs = Player(sport, bills, "Josh Allen");
        db.Players.AddRange(ours, theirs);
        await db.SaveChangesAsync();

        source.Report = [new CanonicalInjury("3046779", "Josh Allen", "LB", "30", "Buffalo Bills", "Questionable", null)];

        await Service(db, source).RefreshAsync(sport.Key, InjuryReportService.JobKey, CancellationToken.None);

        Assert.Null((await db.Players.AsNoTracking().SingleAsync(p => p.Id == ours.Id)).InjuryStatus);
        Assert.Equal("Questionable", (await db.Players.AsNoTracking().SingleAsync(p => p.Id == theirs.Id)).InjuryStatus);
    }
}
