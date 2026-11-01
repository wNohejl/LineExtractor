using System.Text.Json;
using System.Text.Json.Nodes;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The live state ESPN's scoreboard already carries — clock, period, situation, linescore — and
/// which the parser used to throw away. Read from captures taken during live NFL games; the MLB
/// shape is hand-written from ESPN's field names.
/// </summary>
public class EspnLiveStateTests
{
    private static string Path(string fixture)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

    private static JsonElement Load(string fixture)
        => JsonDocument.Parse(File.ReadAllText(Path(fixture))).RootElement.Clone();

    private static JsonElement Edited(string fixture, string eventId, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path(fixture)))!;
        var ev = root["events"]!.AsArray().Single(e => (string?)e!["id"] == eventId)!;
        edit(ev["competitions"]![0]!.AsObject());
        return JsonDocument.Parse(root.ToJsonString()).RootElement.Clone();
    }

    [Fact]
    public void ALiveNflEventCarriesItsClockSituationAndLinescore()
    {
        var game = EspnStatsAdapter.ParseScoreboard(Load("espn.nfl.scoreboard.live.json"), "nfl")
            .Single(g => g.SourceGameId == "401873032");

        var live = game.Live;
        Assert.NotNull(live);
        Assert.Equal(2, live.Period);
        Assert.Equal("8:26", live.DisplayClock);
        Assert.Equal(506, live.ClockSeconds);
        Assert.Equal("8:26 - 2nd", live.StatusDetail);
        Assert.Equal([3, 0], live.HomeLinescore);
        Assert.Equal([3, 3], live.AwayLinescore);

        Assert.Equal("24", live.PossessionSourceTeamId);
        Assert.Equal(1, live.Down);
        Assert.Equal(10, live.Distance);
        Assert.Equal(80, live.YardLine);
        Assert.Equal("1st & 10 at LAC 20", live.DownDistanceText);
        Assert.Equal("LAC 20", live.PossessionText);
        Assert.False(live.IsRedZone);
        Assert.Equal(3, live.HomeTimeouts);
        Assert.Equal(2, live.AwayTimeouts);
        Assert.Equal("Official Timeout at 08:26.", live.LastPlay);

        // Football's situation is all columns; nothing sport-specific is left over for the bag.
        Assert.Null(live.SituationJson);
    }

    [Fact]
    public void AFinalCarriesItsPeriodClockAndLinescoreButNoSituation()
    {
        var game = EspnStatsAdapter.ParseScoreboard(Load("espn.nfl.scoreboard.live.json"), "nfl")
            .Single(g => g.SourceGameId == "401873031");

        var live = game.Live;
        Assert.NotNull(live);
        Assert.Equal(5, live.Period);
        Assert.Equal("0:00", live.DisplayClock);
        Assert.Equal("Final/OT", live.StatusDetail);
        Assert.Equal([0, 14, 9, 13, 7], live.HomeLinescore);
        Assert.Equal([13, 0, 8, 15, 0], live.AwayLinescore);

        Assert.Null(live.PossessionSourceTeamId);
        Assert.Null(live.Down);
        Assert.Null(live.Distance);
        Assert.Null(live.YardLine);
        Assert.Null(live.DownDistanceText);
        Assert.Null(live.PossessionText);
        Assert.Null(live.IsRedZone);
        Assert.Null(live.HomeTimeouts);
        Assert.Null(live.AwayTimeouts);
        Assert.Null(live.LastPlay);
        Assert.Null(live.SituationJson);
    }

    [Fact]
    public void AScheduledEventHasNoSituation()
    {
        var game = EspnStatsAdapter.ParseScoreboard(Load("espn.nfl.scoreboard.live.json"), "nfl")
            .Single(g => g.SourceGameId == "401873035");

        var live = game.Live;
        Assert.NotNull(live);
        Assert.Equal("11/1 - 8:20 PM EST", live.StatusDetail);

        // Period 0 and a 0:00 clock before kick-off are placeholders, not a state of play.
        Assert.Null(live.Period);
        Assert.Null(live.DisplayClock);
        Assert.Null(live.ClockSeconds);
        Assert.Null(live.HomeLinescore);
        Assert.Null(live.PossessionSourceTeamId);
        Assert.Null(live.Down);
        Assert.Null(live.LastPlay);
        Assert.Null(live.SituationJson);
    }

    [Fact]
    public void ALiveMlbEventKeepsItsCountRunnersAndMatchupInTheSituationBag()
    {
        var game = EspnStatsAdapter.ParseScoreboard(Load("espn.mlb.scoreboard.live.json"), "mlb").Single();

        var live = game.Live;
        Assert.NotNull(live);
        Assert.Equal(7, live.Period);
        Assert.Equal("Top 7th", live.StatusDetail);
        Assert.Equal([1, 0, 0, 2, 0, 0, 0], live.AwayLinescore);
        Assert.Equal("Pasquantino singled to right, Garcia to third.", live.LastPlay);
        Assert.Null(live.Down);
        Assert.Null(live.PossessionSourceTeamId);

        Assert.NotNull(live.SituationJson);
        var situation = JsonNode.Parse(live.SituationJson)!;
        Assert.Equal(2, (int)situation["balls"]!);
        Assert.Equal(1, (int)situation["strikes"]!);
        Assert.Equal(1, (int)situation["outs"]!);
        Assert.True((bool)situation["onFirst"]!);
        Assert.False((bool)situation["onSecond"]!);
        Assert.True((bool)situation["onThird"]!);
        Assert.Equal("Bobby Witt Jr.", (string)situation["batter"]!);
        Assert.Equal("Tarik Skubal", (string)situation["pitcher"]!);
    }

    [Fact]
    public void ALiveEventWithNoSituationStillParses()
    {
        var root = Edited("espn.nfl.scoreboard.live.json", "401873032", c => c.Remove("situation"));

        var game = EspnStatsAdapter.ParseScoreboard(root, "nfl").Single(g => g.SourceGameId == "401873032");

        Assert.Equal("live", game.Status);
        Assert.Equal(2, game.Live!.Period);
        Assert.Null(game.Live.Down);
        Assert.Null(game.Live.LastPlay);
    }

    [Theory]
    [InlineData("\"not an object\"")]
    [InlineData("null")]
    [InlineData("{\"down\":\"first\",\"yardLine\":null,\"possession\":24,\"isRedZone\":\"no\",\"lastPlay\":\"x\",\"balls\":\"two\",\"batter\":\"Witt\",\"onFirst\":1}")]
    public void AMalformedSituationParsesWithoutThrowing(string situationJson)
    {
        var root = Edited("espn.nfl.scoreboard.live.json", "401873032",
            c =>
            {
                c["situation"] = JsonNode.Parse(situationJson);
                c["competitors"]![0]!["linescores"] = JsonNode.Parse("[{\"value\":\"three\"},7,null]");
                c["status"]!["clock"] = "soon";
                c["status"]!["period"] = "second";
            });

        var game = EspnStatsAdapter.ParseScoreboard(root, "nfl").Single(g => g.SourceGameId == "401873032");

        Assert.NotNull(game.Live);
        Assert.Null(game.Live.Period);
        Assert.Null(game.Live.ClockSeconds);
        Assert.Null(game.Live.Down);
        Assert.Null(game.Live.IsRedZone);
        Assert.Null(game.Live.LastPlay);
    }

    /// <summary>
    /// ESPN documents none of these strings, and the columns that hold them are short. A status
    /// line or situation longer than its column would fail the whole save, so each is cut to fit.
    /// </summary>
    [Fact]
    public void AnOverLongStringIsCutToItsColumn()
    {
        var root = Edited("espn.nfl.scoreboard.live.json", "401873032",
            c =>
            {
                c["status"]!["type"]!["shortDetail"] = new string('d', 200);
                c["status"]!["displayClock"] = new string('c', 40);
                c["situation"]!["downDistanceText"] = new string('x', 200);
                c["situation"]!["possessionText"] = new string('p', 200);
            });

        var live = EspnStatsAdapter.ParseScoreboard(root, "nfl").Single(g => g.SourceGameId == "401873032").Live!;

        Assert.Equal(new string('d', Game.StatusDetailLength), live.StatusDetail);
        Assert.Equal(new string('c', Game.DisplayClockLength), live.DisplayClock);
        Assert.Equal(new string('x', Game.DownDistanceTextLength), live.DownDistanceText);
        Assert.Equal(new string('p', Game.PossessionTextLength), live.PossessionText);
    }

    /// <summary>The lengths the adapter cuts to are the columns' own.</summary>
    [Fact]
    public void TheLiveColumnsAreAsLongAsTheAdapterCutsTo()
    {
        using var db = new LineOps.Data.LineOpsDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<LineOps.Data.LineOpsDbContext>()
                .UseNpgsql("Host=unused").Options);
        var game = db.Model.FindEntityType(typeof(Game))!;

        Assert.Equal(Game.DisplayClockLength, game.FindProperty(nameof(Game.DisplayClock))!.GetMaxLength());
        Assert.Equal(Game.StatusDetailLength, game.FindProperty(nameof(Game.StatusDetail))!.GetMaxLength());
        Assert.Equal(Game.DownDistanceTextLength, game.FindProperty(nameof(Game.DownDistanceText))!.GetMaxLength());
        Assert.Equal(Game.PossessionTextLength, game.FindProperty(nameof(Game.PossessionText))!.GetMaxLength());
    }
}
