using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Tests;

/// <summary>
/// The Game window's title is a scoreboard, not a sentence: away over home, each team's mark
/// and name with its score trailing, and the loser of a finished game a rung quieter than the
/// winner. GamePanel cannot render without a database, so the scoreboard is its own component.
/// </summary>
public class ScoreboardTests : DeskTestContext
{
    private static Game Steelers_at_Saints(GameStatus status, int? away, int? home) => new()
    {
        Sport = new Sport { Key = "nfl" },
        AwayTeamId = 23,
        AwayTeam = new Team { Id = 23, Name = "Pittsburgh Steelers", Abbrev = "PIT" },
        HomeTeamId = 18,
        HomeTeam = new Team { Id = 18, Name = "New Orleans Saints", Abbrev = "NO" },
        StartsAt = new DateTimeOffset(2026, 10, 26, 17, 0, 0, TimeSpan.Zero),
        Status = status,
        AwayScore = away,
        HomeScore = home,
    };

    // What a screen reader hears from the heading: the text with every aria-hidden part (the
    // marks, the links' "team" shortcuts) taken out, whitespace folded.
    private static string Spoken(IElement root)
    {
        var clone = (IElement)root.Clone(deep: true);
        foreach (var hidden in clone.QuerySelectorAll("[aria-hidden=true]").ToList())
            hidden.Remove();
        return string.Join(' ', clone.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Final_game_dims_the_loser()
    {
        var cut = RenderComponent<Scoreboard>(p => p.Add(s => s.Game, Steelers_at_Saints(GameStatus.Final, 16, 10)));

        var rows = cut.FindAll(".scoreboard__row");
        Assert.Equal(2, rows.Count);

        // Away first, then home, each with its score trailing.
        Assert.Contains("Pittsburgh Steelers", rows[0].TextContent);
        Assert.Equal("16", rows[0].QuerySelector(".scoreboard__score")!.TextContent.Trim());
        Assert.Contains("New Orleans Saints", rows[1].TextContent);
        Assert.Equal("10", rows[1].QuerySelector(".scoreboard__score")!.TextContent.Trim());

        // The winner keeps full weight; the loser drops a rung.
        Assert.DoesNotContain("scoreboard__row--loser", rows[0].ClassList);
        Assert.Contains("scoreboard__row--loser", rows[1].ClassList);

        // Each row carries the team's mark, at the large size.
        Assert.All(rows, r => Assert.NotNull(r.QuerySelector(".teammark.teammark--lg")));

        // Heading semantics survive the shape: the name still reads as one line.
        Assert.Equal("Pittsburgh Steelers 16, New Orleans Saints 10", Spoken(cut.Find(".scoreboard")));
    }

    [Theory]
    [InlineData(GameStatus.Live, 7, 3)]
    [InlineData(GameStatus.Live, 0, 0)]
    [InlineData(GameStatus.Final, 20, 20)]
    public void Live_game_keeps_both_rows_primary(GameStatus status, int away, int home)
    {
        var cut = RenderComponent<Scoreboard>(p => p.Add(s => s.Game, Steelers_at_Saints(status, away, home)));

        var rows = cut.FindAll(".scoreboard__row");
        Assert.Equal(2, rows.Count);
        Assert.Empty(cut.FindAll(".scoreboard__row--loser"));
        Assert.Equal(2, cut.FindAll(".scoreboard__score").Count);
    }

    [Theory]
    [InlineData(GameStatus.Scheduled)]
    [InlineData(GameStatus.Postponed)]
    public void Scheduled_game_shows_no_scores(GameStatus status)
    {
        // ESPN writes zeros for a fixture that has not started (or never will); they are not a result.
        var game = Steelers_at_Saints(status, 0, 0);
        var cut = RenderComponent<Scoreboard>(p => p.Add(s => s.Game, game));

        Assert.Equal(2, cut.FindAll(".scoreboard__row").Count);
        Assert.Empty(cut.FindAll(".scoreboard__score"));
        Assert.Empty(cut.FindAll(".scoreboard__row--loser"));

        // The start time is the title's Detail line, once; nothing trails the rows.
        Assert.Empty(cut.FindAll(".scoreboard__time"));
        Assert.DoesNotContain(LineOps.Web.Components.DisplayTime.Stamp(game.StartsAt), cut.Markup);

        Assert.Equal("Pittsburgh Steelers at New Orleans Saints", Spoken(cut.Find(".scoreboard")));
    }

    [Fact]
    public void Team_names_link_to_their_windows()
    {
        var opened = new List<int>();
        var cut = RenderComponent<Scoreboard>(p => p
            .Add(s => s.Game, Steelers_at_Saints(GameStatus.Final, 16, 10))
            .Add(s => s.OnTeam, EventCallback.Factory.Create<int>(this, opened.Add)));

        var links = cut.FindAll("button.desklink");
        Assert.Equal(2, links.Count);
        Assert.Contains("Pittsburgh Steelers", links[0].TextContent);
        Assert.Contains("New Orleans Saints", links[1].TextContent);

        links[1].Click();
        cut.FindAll("button.desklink")[0].Click();

        Assert.Equal([18, 23], opened);
    }

    [Fact]
    public void Compact_reads_on_one_line()
    {
        var cut = RenderComponent<Scoreboard>(p => p
            .Add(s => s.Game, Steelers_at_Saints(GameStatus.Final, 16, 10))
            .Add(s => s.Compact, true));

        var board = cut.Find(".scoreboard");
        Assert.Contains("scoreboard--compact", board.ClassList);

        // Away · at · home, in that order, with the scores beside the names; the middots are
        // for the eye, so a screen reader hears "16 at New Orleans".
        var parts = board.Children.Where(c => c.ClassList.Contains("scoreboard__at")).ToList();
        Assert.Equal(["·", "at", "·"], parts.Select(c => c.TextContent.Trim()));
        Assert.Equal("true", parts[0].GetAttribute("aria-hidden"));
        Assert.Null(parts[1].GetAttribute("aria-hidden"));
        Assert.Equal("true", parts[2].GetAttribute("aria-hidden"));
        Assert.Equal("Pittsburgh Steelers 16 at New Orleans Saints 10", Spoken(board));

        // The loser is still quieter on one line.
        Assert.Contains("scoreboard__row--loser", cut.FindAll(".scoreboard__row")[1].ClassList);
    }
}
