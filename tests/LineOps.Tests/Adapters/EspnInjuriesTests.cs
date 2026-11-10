using System.Net;
using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Ingestion.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Adapters;

/// <summary>
/// ESPN's league injury report (<c>football/nfl/injuries</c>), pinned against the response of
/// 9 November 2026 trimmed to the Vikings and the Bills: Murray ruled out of Monday's game,
/// Wentz back to Active. The athlete carries no <c>id</c> on this endpoint — his ESPN id is in
/// his player-card link, and that is where it is read from.
/// </summary>
public class EspnInjuriesTests
{
    private static string Fixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.nfl.injuries.json"));

    private static List<CanonicalInjury> Report()
    {
        using var doc = JsonDocument.Parse(Fixture());
        return EspnStatsAdapter.ParseInjuries(doc.RootElement).ToList();
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static (IInjurySource Source, RecordingHandler Handler) Adapter()
    {
        var handler = new RecordingHandler(Fixture());
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/sports/") };
        return (new EspnStatsAdapter(http, NullLogger<EspnStatsAdapter>.Instance), handler);
    }

    [Fact]
    public void Murray_is_out_with_the_clubs_word_on_it()
    {
        var murray = Report().Single(i => i.FullName == "Kyler Murray");

        Assert.Equal("3917315", murray.SourcePlayerId);
        Assert.Equal("Out", murray.Status);
        Assert.Equal("QB", murray.Position);
        Assert.Equal("16", murray.SourceTeamId);
        Assert.Equal("Minnesota Vikings", murray.TeamName);
        Assert.Equal(
            "Head coach Kevin O'Connell said Saturday that Murray (hamstring) has been ruled out for Monday's game versus the Bills.",
            murray.Note);
    }

    [Fact]
    public void Wentz_is_active()
    {
        var wentz = Report().Single(i => i.FullName == "Carson Wentz");

        Assert.Equal("2573079", wentz.SourcePlayerId);
        Assert.Equal("Active", wentz.Status);
    }

    [Fact]
    public void Every_club_on_the_report_is_read()
    {
        var report = Report();

        Assert.Equal(7, report.Count);
        Assert.Equal(3, report.Count(i => i.TeamName == "Buffalo Bills"));
        Assert.Contains(report, i => i.Status == "Injured Reserve");
        Assert.Contains(report, i => i.Status == "Questionable");
    }

    /// <summary>ESPN writes "ir" as the note on an injured-reserve entry; that says nothing the status does not.</summary>
    [Fact]
    public void A_note_that_only_restates_the_status_is_dropped()
        => Assert.Null(Report().Single(i => i.FullName == "Demond Claiborne").Note);

    [Theory]
    [InlineData("Short.", "Short.")]
    [InlineData("Murray (hamstring) was ruled out Saturday. He is week to week.", "Murray (hamstring) was ruled out Saturday.")]
    [InlineData("O'Connell said Murray is day-to-day vs. the Bills", "O'Connell said Murray is day-to-day vs. the Bills")]
    [InlineData("ir", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_note_is_its_first_sentence(string? comment, string? note)
        => Assert.Equal(note, EspnStatsAdapter.InjuryNote(comment));

    [Fact]
    public async Task The_report_is_one_request_for_the_league()
    {
        var (source, handler) = Adapter();

        var report = await source.FetchInjuriesAsync("nfl", CancellationToken.None);

        Assert.Equal(7, report.Count);
        Assert.Equal(["https://site.api.espn.com/apis/site/v2/sports/football/nfl/injuries"], handler.Urls);
    }

    [Theory]
    [InlineData("ufc")]
    [InlineData("mlb")]
    [InlineData("cricket")]
    public async Task A_league_without_a_report_asks_nothing(string sport)
    {
        var (source, handler) = Adapter();

        Assert.Empty(await source.FetchInjuriesAsync(sport, CancellationToken.None));
        Assert.Empty(handler.Urls);
    }
}
