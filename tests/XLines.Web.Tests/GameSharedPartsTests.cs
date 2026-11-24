using Bunit;
using XLines.Core.Analytics;
using XLines.Desk.Testing;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The parts every game window shares: what a period is called in each league
/// (<see cref="Periods"/>), the one reader of a stored line score (<see cref="Linescores"/>), the
/// line score itself, the play log and the field's one-line geometry. Each is held here on its
/// own, apart from the Gamecast window that first drew them, so a second window can take one
/// without taking the Gamecast's tests with it.
/// </summary>
public class GameSharedPartsTests : DeskTestContext
{
    [Theory]
    [InlineData("nfl", 1, "1st quarter", "1", "Q1")]
    [InlineData("nfl", 4, "4th quarter", "4", "Q4")]
    [InlineData("nfl", 5, "Overtime", "OT", "OT")]
    [InlineData("nba", 6, "2nd overtime", "2OT", "2OT")]
    [InlineData("mlb", 7, "7th inning", "7", null)]
    [InlineData("mlb", 11, "11th inning", "11", null)]
    [InlineData("ufc", 2, "Round 2", "2", "R2")]
    [InlineData("nhl", 3, "3rd period", "3", null)]
    public void A_period_is_called_what_its_league_calls_it(string sport, int period, string name, string column, string? mark)
    {
        Assert.Equal(name, Periods.Name(sport, period));
        Assert.Equal(column, Periods.Column(sport, period));
        Assert.Equal(mark, Periods.Mark(sport, period));
    }

    [Fact]
    public void A_line_score_ends_in_runs_for_baseball_and_a_total_elsewhere()
    {
        Assert.Equal("R", Periods.TotalColumn("mlb"));
        Assert.Equal("T", Periods.TotalColumn("nfl"));
    }

    [Fact]
    public void A_stored_line_score_is_read_side_by_side_and_a_side_with_a_hole_is_empty()
    {
        var (home, away) = Linescores.Parse("""{"home":[0,3],"away":[7,0]}""");
        Assert.Equal([0, 3], home);
        Assert.Equal([7, 0], away);

        // A hole would shift every later period one column left, so the side is dropped whole.
        var (holed, whole) = Linescores.Parse("""{"home":[0,null,3],"away":[7,0,0]}""");
        Assert.Empty(holed);
        Assert.Equal([7, 0, 0], whole);

        Assert.Equal(([], []), Linescores.Parse(null));
        Assert.Equal(([], []), Linescores.Parse("not json"));
    }

    [Fact]
    public void The_line_score_draws_any_league_by_its_own_columns()
    {
        var cut = RenderComponent<Linescore>(p => p
            .Add(l => l.Sport, "mlb")
            .Add(l => l.AwayAbbrev, "NYY")
            .Add(l => l.HomeAbbrev, "BOS")
            .Add(l => l.Away, [0, 1, 0, 0, 2, 0, 0, 0, 0, 1])
            .Add(l => l.Home, [1, 0, 0, 0, 0, 2, 0, 0, 0])
            .Add(l => l.AwayTotal, 4)
            .Add(l => l.HomeTotal, 3));

        var heads = cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal("10", heads[^2]);
        Assert.Equal("R", heads[^1]);

        // The home side has not batted in the tenth: its cell is blank, not a zero.
        var home = cut.Find("tr[data-line='BOS']").QuerySelectorAll("td").Select(c => c.TextContent).ToArray();
        Assert.Equal(string.Empty, home[9]);
        Assert.Equal("3", home[^1]);
        Assert.Contains("linescore__ahead", cut.Find("tr[data-line='NYY']").ClassName);
    }

    [Fact]
    public void The_line_score_draws_nothing_before_a_period_has_been_played()
    {
        var cut = RenderComponent<Linescore>(p => p
            .Add(l => l.AwayAbbrev, "PHI")
            .Add(l => l.HomeAbbrev, "WSH")
            .Add(l => l.Away, [])
            .Add(l => l.Home, []));

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void The_play_log_draws_its_plays_in_the_order_given_and_stamps_them_by_league()
    {
        var plays = new[]
        {
            FakeGamecastData.Play(2, 1, "Second"),
            FakeGamecastData.Play(1, 1, "First", scoring: true)
        };

        var cut = RenderComponent<PlayLog>(p => p.Add(l => l.Plays, plays).Add(l => l.Sport, "nfl"));

        var rows = cut.FindAll(".gc__play");
        Assert.Equal(new string?[] { "2", "1" }, rows.Select(r => r.GetAttribute("data-seq")).ToArray());
        Assert.Equal("Q2 5:00", rows[0].QuerySelector(".gc__when")!.TextContent);
        Assert.Contains("gc__play--score", rows[1].ClassName);

        // A league with no mark of its own is stamped with the clock alone.
        Assert.Equal("5:00", PlayLog.When("mlb", plays[0]));
    }

    private static XLines.Data.CrossReference.PlayerForm Player(
        int id, string name, Dictionary<string, double> totals, bool rate = false)
        => new(id, name, "QB", IsHome: true, Appearances: 2, LastPlayed: DateTimeOffset.UtcNow,
            Totals: totals,
            Counts: totals.Keys.ToDictionary(k => k, _ => 2),
            Counting: totals.Keys.ToDictionary(k => k, _ => !rate));

    /// <summary>
    /// The roster table's shared rules: a count is totalled with its separator, a rate is averaged
    /// and headed "/g", and a player with no reading for a stat is a dash rather than a zero.
    /// </summary>
    [Fact]
    public void The_roster_table_totals_counts_averages_rates_and_dashes_what_was_not_read()
    {
        var side = new[]
        {
            Player(1, "A. Passer", new() { ["YDS"] = 1234 }),
            Player(2, "B. Runner", new() { ["AVG"] = 9 }, rate: true)
        };

        var cut = RenderComponent<RosterTable>(p => p.Add(t => t.Side, side));

        var heads = cut.FindAll("th").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Contains(heads, h => h.EndsWith("/g", StringComparison.Ordinal));
        Assert.DoesNotContain("Last", heads);

        var passer = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("A. Passer")).TextContent;
        Assert.Contains("1,234", passer);
        Assert.Contains("—", passer);
        Assert.Contains("4.500", cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("B. Runner")).TextContent);
    }

    [Fact]
    public void The_roster_table_takes_the_callers_name_cell_last_played_column_and_row_press()
    {
        var side = new[] { Player(7, "C. Catcher", new() { ["REC"] = 5 }) };
        int? pressed = null;

        var cut = RenderComponent<RosterTable>(p => p
            .Add(t => t.Side, side)
            .Add(t => t.ShowLastPlayed, true)
            .Add(t => t.NameContent, player => $"<b class=\"mine\">{player.Name}</b>")
            .Add(t => t.RowClass, player => player.PlayerId == 7 ? "is-open" : null)
            .Add(t => t.OnRowClick, (XLines.Data.CrossReference.PlayerForm player) => pressed = player.PlayerId));

        Assert.Contains("Last", cut.FindAll("th").Select(h => h.TextContent.Trim()));
        Assert.Equal("C. Catcher", cut.Find("b.mine").TextContent);
        Assert.Contains("deskgrid--clickable", cut.Markup);

        var row = cut.Find("tbody tr");
        Assert.Contains("is-open", row.ClassName);
        row.Click();
        Assert.Equal(7, pressed);
    }

    [Fact]
    public void A_stretch_of_field_is_measured_from_the_away_goal_and_never_leaves_the_field()
    {
        Assert.Equal((25d, 41d), FieldGeometry.Span(75, 34));
        Assert.Equal((25d, 41d), FieldGeometry.Span(34, 75));
        Assert.Equal((98d, 2d), FieldGeometry.Span(0, 0));
        Assert.Null(FieldGeometry.Span(75, null));
        Assert.Null(FieldGeometry.Span(null, 34));
    }
}
