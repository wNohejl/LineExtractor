using System.Text.Json;
using System.Text.Json.Nodes;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// ESPN stamps every event with the season it belongs to. The adapter used to read that block
/// only to throw preseason away; it now carries the year and type through, because a season
/// filter needs what a date alone cannot say — that a February playoff is the prior year's.
/// </summary>
public class EspnSeasonStampTests
{
    private static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "espn.scoreboard.json");

    /// <summary>The real scoreboard fixture, with a season block stamped onto every event.</summary>
    private static JsonDocument ScoreboardWithSeason(int? year, int? type)
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath))!;

        foreach (var e in root["events"]!.AsArray())
        {
            if (year is null && type is null)
            {
                e!.AsObject().Remove("season");
                continue;
            }

            var season = new JsonObject();
            if (year is { } y) season["year"] = y;
            if (type is { } t) season["type"] = t;
            e!["season"] = season;
        }

        return JsonDocument.Parse(root.ToJsonString());
    }

    [Fact]
    public void A_postseason_event_carries_its_season_year_and_type()
    {
        using var doc = ScoreboardWithSeason(2025, 3);

        var games = EspnStatsAdapter.ParseScoreboard(doc.RootElement, "nfl").ToList();

        Assert.NotEmpty(games);
        Assert.All(games, g =>
        {
            Assert.Equal(2025, g.SeasonYear);
            Assert.Equal(SeasonType.Postseason, g.SeasonType);
        });
    }

    [Fact]
    public void A_regular_season_event_is_stamped_regular()
    {
        using var doc = ScoreboardWithSeason(2026, 2);

        var games = EspnStatsAdapter.ParseScoreboard(doc.RootElement, "mlb").ToList();

        Assert.NotEmpty(games);
        Assert.All(games, g => Assert.Equal(SeasonType.Regular, g.SeasonType));
    }

    [Fact]
    public void Preseason_is_still_excluded_rather_than_stamped()
    {
        using var doc = ScoreboardWithSeason(2026, 1);

        Assert.Empty(EspnStatsAdapter.ParseScoreboard(doc.RootElement, "mlb"));
    }

    [Fact]
    public void An_event_with_no_season_block_leaves_the_stamp_to_the_calendar()
    {
        using var doc = ScoreboardWithSeason(null, null);

        var games = EspnStatsAdapter.ParseScoreboard(doc.RootElement, "mlb").ToList();

        Assert.NotEmpty(games);
        Assert.All(games, g =>
        {
            Assert.Null(g.SeasonYear);
            Assert.Null(g.SeasonType);
        });
    }

    [Fact]
    public void An_unknown_season_type_is_left_unstamped_rather_than_guessed()
    {
        using var doc = ScoreboardWithSeason(2026, 4);

        var games = EspnStatsAdapter.ParseScoreboard(doc.RootElement, "mlb").ToList();

        Assert.NotEmpty(games);
        Assert.All(games, g =>
        {
            Assert.Equal(2026, g.SeasonYear);
            Assert.Null(g.SeasonType);
        });
    }
}
