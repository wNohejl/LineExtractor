using System.Net;
using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Ingestion.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The gamecast parse, pinned against a summary and a probabilities page captured during a
/// live NFL game (LAC @ LAR, 1 Nov 2026, second quarter).
///
/// <para>
/// The capture's shape is the point. While a drive is the latest one ESPN sends it twice — as
/// <c>drives.current</c> and again as the last of <c>drives.previous</c> — so the parse has to
/// come out with one drive and one copy of each play, not two. Win probability arrives in a
/// separate array paired by <c>playId</c>, and the core API's spread and total probabilities
/// name their play only by a <c>$ref</c> URL.
/// </para>
/// </summary>
public class EspnGamecastTests
{
    private const string GameId = "401873032";

    private static JsonElement Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static CanonicalGamecast Live()
        => EspnStatsAdapter.ParseGamecast(Load("espn.summary.live.json"), GameId);

    [Fact]
    public void ParseGamecast_StampsTheGameAndItsState()
    {
        var cast = Live();

        Assert.Equal(GameId, cast.SourceGameId);
        Assert.Equal("live", cast.State);
    }

    [Fact]
    public void ParseGamecast_ListsDrivesInOrderWithTheCurrentOneOnce()
    {
        var drives = Live().Drives;

        // Four drives, not five: the current drive is also the last of the previous ones.
        Assert.Equal(["4018730321", "4018730322", "4018730326", "40187303210"],
            drives.Select(d => d.SourceDriveId));
        Assert.Equal([1, 2, 3, 4], drives.Select(d => d.Sequence));
        Assert.Equal(["40187303210"], drives.Where(d => d.IsCurrent).Select(d => d.SourceDriveId));
    }

    [Fact]
    public void ParseGamecast_ReadsADrivesSummaryAndItsEnds()
    {
        var drive = Live().Drives.Single(d => d.SourceDriveId == "4018730326");

        Assert.Equal("14", drive.SourceTeamId);
        Assert.Equal("Field Goal", drive.Result);
        Assert.True(drive.IsScore);
        Assert.False(drive.IsCurrent);
        Assert.Equal(1, drive.StartPeriod);
        Assert.Equal("7:11", drive.StartClock);
        Assert.Equal(1, drive.EndPeriod);
        Assert.Equal("5:42", drive.EndClock);
        Assert.Equal(8, drive.Yards);
        Assert.Equal(4, drive.OffensivePlays);
        Assert.Equal("1:29", drive.TimeElapsed);
        Assert.Equal("4 plays, 8 yards, 1:29", drive.Description);
        Assert.Equal(81, drive.StartYardLine);
        Assert.Equal(89, drive.EndYardLine);
    }

    [Fact]
    public void ParseGamecast_EmitsEachPlayOnceInSequenceOrder()
    {
        var plays = Live().Plays;

        Assert.Equal(9, plays.Count);
        Assert.Equal(plays.Count, plays.Select(p => p.SourcePlayId).Distinct().Count());
        Assert.Equal(plays.OrderBy(p => p.Sequence).Select(p => p.SourcePlayId),
            plays.Select(p => p.SourcePlayId));
    }

    [Fact]
    public void ParseGamecast_ReadsAPlayInFull()
    {
        var play = Live().Plays.Single(p => p.SourcePlayId == "4018730321318");

        // Sequence numbers arrive as strings.
        Assert.Equal(131800L, play.Sequence);
        Assert.Equal("40187303210", play.SourceDriveId);
        Assert.Equal(2, play.Period);
        Assert.Equal("10:06", play.Clock);
        Assert.Equal(606, play.ClockSeconds);
        Assert.Equal("53", play.TypeId);
        Assert.Equal("Kickoff", play.Type);
        Assert.StartsWith("C.Dicker kicks 59 yards", play.Text);
        Assert.Equal(3, play.HomeScore);
        Assert.Equal(6, play.AwayScore);
        Assert.Equal(22, play.Yardage);
        Assert.False(play.IsScoring);
        Assert.False(play.IsPenalty);
        Assert.False(play.IsTurnover);

        // The receiving side is the offence on a kickoff, though the kicking side starts it.
        Assert.Equal("14", play.SourceTeamId);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 21, 56, 56, TimeSpan.Zero), play.Wallclock);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 21, 59, 0, TimeSpan.Zero), play.Modified);
    }

    [Fact]
    public void ParseGamecast_ReadsTheSituationBeforeAndAfterAPlay()
    {
        var play = Live().Plays.Single(p => p.SourcePlayId == "4018730321318");

        Assert.Equal(new CanonicalPlaySituation(0, 0, 65, 65, null, null, "24"), play.Start);
        Assert.Equal(new CanonicalPlaySituation(1, 10, 28, 72, "1st & 10 at LAR 28", "LAR 28", "14"), play.End);
    }

    [Fact]
    public void ParseGamecast_CarriesTheFlagsEspnSets()
    {
        var plays = Live().Plays.ToDictionary(p => p.SourcePlayId);

        Assert.True(plays["401873032207"].IsPenalty);
        Assert.True(plays["401873032242"].IsTurnover);
        Assert.True(plays["401873032689"].IsScoring);
        Assert.Equal(-5, plays["401873032207"].Yardage);
    }

    [Fact]
    public void ParseGamecast_PairsWinProbabilityByPlayId()
    {
        var plays = Live().Plays.ToDictionary(p => p.SourcePlayId);

        Assert.Equal(0.7331m, plays["40187303240"].HomeWinPct);
        Assert.Equal(0.7481m, plays["40187303263"].HomeWinPct);

        // The summary states only the win probability; the rest comes from the core API.
        Assert.All(plays.Values, p => Assert.Null(p.SpreadCoverPctHome));
        Assert.All(plays.Values, p => Assert.Null(p.TotalOverPct));
    }

    [Fact]
    public void ParseGamecast_ASummaryWithNoDrivesIsAnEmptyGamecast()
    {
        // The box-score fixture: no drives, no win probability, no header.
        var cast = EspnStatsAdapter.ParseGamecast(Load("espn.summary.json"), "game-1");

        Assert.Equal("game-1", cast.SourceGameId);
        Assert.Empty(cast.Drives);
        Assert.Empty(cast.Plays);
    }

    [Fact]
    public void ParseProbabilities_KeysEachRowByThePlayInItsRef()
    {
        var rows = EspnStatsAdapter.ParseProbabilities(Load("espn.probabilities.json"));

        Assert.Equal(5, rows.Count);

        var row = rows["40187303240"];
        Assert.Equal("40187303240", row.SourcePlayId);
        Assert.Equal(0.7331m, row.HomeWinPct);
        Assert.Equal(0.5211m, row.SpreadCoverPctHome);
        Assert.Equal(0.4998m, row.TotalOverPct);
    }

    [Fact]
    public void MergeProbabilities_FillsTheSpreadAndTotalOntoTheirPlays()
    {
        var rows = EspnStatsAdapter.ParseProbabilities(Load("espn.probabilities.json"));
        var plays = EspnStatsAdapter.MergeProbabilities(Live(), rows).Plays.ToDictionary(p => p.SourcePlayId);

        Assert.Equal(0.5211m, plays["40187303240"].SpreadCoverPctHome);
        Assert.Equal(0.4998m, plays["40187303240"].TotalOverPct);
        Assert.Equal(0.5119m, plays["40187303263"].SpreadCoverPctHome);
        Assert.Equal(0.7331m, plays["40187303240"].HomeWinPct);

        // A play the page did not cover keeps what it had.
        Assert.Null(plays["4018730321318"].SpreadCoverPctHome);
    }

    [Fact]
    public void ParseProbabilities_AnEmptyPageIsAnEmptyMap()
    {
        using var doc = JsonDocument.Parse("""{ "count": 0, "items": [] }""");

        Assert.Empty(EspnStatsAdapter.ParseProbabilities(doc.RootElement));
    }

    private sealed class RecordingHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(url))
            });
        }
    }

    private static (IGamecastSource Source, RecordingHandler Handler) Adapter()
    {
        var summary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.summary.live.json"));
        var probs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.probabilities.json"));
        var handler = new RecordingHandler(url => url.Contains("/probabilities") ? probs : summary);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/sports/") };

        return (new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance), handler);
    }

    [Fact]
    public async Task FetchGamecastAsync_ReadsTheSummaryForTheGame()
    {
        var (source, handler) = Adapter();

        var cast = await source.FetchGamecastAsync("nfl", GameId, CancellationToken.None);

        Assert.Equal(["https://site.api.espn.com/apis/site/v2/sports/football/nfl/summary?event=401873032"], handler.Urls);
        Assert.NotNull(cast);
        Assert.Equal(9, cast.Plays.Count);
    }

    /// <summary>
    /// One summary, read once, for both the plays and the live box score — the box parsed by the
    /// results pass's own parse, and stating no game: a live summary's status and score are the
    /// scoreboard pass's, not the box's.
    /// </summary>
    [Fact]
    public async Task FetchGamecastWithBoxAsync_ReadsOneSummaryForThePlaysAndTheBox()
    {
        var (source, handler) = Adapter();

        var read = await source.FetchGamecastWithBoxAsync("nfl", GameId, CancellationToken.None);

        Assert.Equal(["https://site.api.espn.com/apis/site/v2/sports/football/nfl/summary?event=401873032"], handler.Urls);
        Assert.NotNull(read);
        Assert.Equal(9, read.Gamecast.Plays.Count);

        Assert.Empty(read.Box.Games);
        Assert.Empty(read.Box.Lines);
        Assert.Equal(0, read.Box.Cost.Requests);
        Assert.Equal(4, read.Box.Players.Count);
        Assert.Equal(4, read.Box.PlayerStats.Count);
        Assert.All(read.Box.PlayerStats, st => Assert.Equal(GameId, st.SourceGameId));

        var herbert = read.Box.PlayerStats.Single(st => st.SourcePlayerId == "4038941");
        Assert.Equal("24", herbert.SourceTeamId);
        Assert.Contains("\"YDS\":\"76\"", herbert.StatLineJson);
    }

    [Fact]
    public async Task FetchProbabilitiesAsync_ReadsTheCorePageWhole()
    {
        var (source, handler) = Adapter();

        var rows = await source.FetchProbabilitiesAsync("nfl", GameId, CancellationToken.None);

        Assert.Equal(
            ["https://sports.core.api.espn.com/v2/sports/football/leagues/nfl/events/401873032/competitions/401873032/probabilities?limit=1000"],
            handler.Urls);
        Assert.Equal(5, rows.Count);
    }

    /// <summary>The day's board, then the one game's summary — never every final on the day.</summary>
    [Fact]
    public async Task FetchBoxScoreAsync_ReadsTheBoardThenOnlyThatGamesSummary()
    {
        var board = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.scoreboard.json"));
        var summary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.summary.json"));
        var handler = new RecordingHandler(url => url.Contains("/scoreboard") ? board : summary);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/sports/") };
        IGamecastSource source = new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance);

        var final = await source.FetchBoxScoreAsync("mlb", new DateOnly(2026, 9, 20), "401816200", CancellationToken.None);

        Assert.Equal("401816200", Assert.Single(final.Games).SourceGameId);
        Assert.Contains("baseball/mlb/scoreboard?dates=20260920", handler.Urls[0]);
        Assert.Contains("baseball/mlb/summary?event=401816200", handler.Urls[1]);
        Assert.DoesNotContain(handler.Urls, u => u.Contains("summary?event=401816201"));

        handler.Urls.Clear();

        // Not final on the board: nothing to write, and no summary asked for.
        var scheduled = await source.FetchBoxScoreAsync("mlb", new DateOnly(2026, 9, 20), "401816201", CancellationToken.None);

        Assert.Empty(scheduled.Games);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task Fetch_ALeagueThePortCannotAddressCostsNoRequest()
    {
        var (source, handler) = Adapter();

        Assert.Null(await source.FetchGamecastAsync("cricket", GameId, CancellationToken.None));
        Assert.Empty(await source.FetchProbabilitiesAsync("cricket", GameId, CancellationToken.None));
        Assert.Empty(handler.Urls);
    }
}
