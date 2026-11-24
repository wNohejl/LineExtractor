using Bunit;
using XLines.Desk.Testing;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The Gamecast's team comparison: the rows in the order they come, away figure left and home
/// right, each one sentence to assistive tech with its bar hidden, and nothing at all with no rows.
/// </summary>
public class TeamComparisonTests : DeskTestContext
{
    private static readonly ComparisonRow[] Rows =
    [
        new("Time of possession", "14:32", "9:28", 60.5, 39.5),
        new("Total yards", "142", "208", 40.6, 59.4),
        new("Turnovers", "0", "0", 0, 0, HigherIsBetter: false),
    ];

    private IRenderedComponent<TeamComparison> Render(IReadOnlyList<ComparisonRow> rows)
        => RenderComponent<TeamComparison>(p => p
            .Add(s => s.Rows, rows).Add(s => s.AwayAbbrev, "PIT").Add(s => s.HomeAbbrev, "PHI").Add(s => s.Sport, "nfl"));

    [Fact]
    public void The_rows_render_in_order_away_left_and_home_right()
    {
        var cut = Render(Rows);

        var rows = cut.FindAll(".teamcmp__row");
        Assert.Equal(new[] { "Time of possession", "Total yards", "Turnovers" }, rows.Select(r => r.QuerySelector(".teamcmp__label")!.TextContent));
        Assert.Equal("142", rows[1].QuerySelector(".teamcmp__away")!.TextContent);
        Assert.Equal("208", rows[1].QuerySelector(".teamcmp__home")!.TextContent);
    }

    [Fact]
    public void Each_row_reads_as_one_sentence_and_its_bar_is_hidden()
    {
        var cut = Render(Rows);

        var yards = cut.FindAll(".teamcmp__row")[1];
        Assert.Equal("Total yards: PIT 142, PHI 208", yards.QuerySelector(".sr-only")!.TextContent);
        Assert.Equal("true", yards.QuerySelector(".teamcmp__bar")!.GetAttribute("aria-hidden"));
        Assert.Equal("true", yards.QuerySelector(".teamcmp__away")!.GetAttribute("aria-hidden"));
        Assert.Equal("true", cut.Find(".teamcmp__head").GetAttribute("aria-hidden"));
        Assert.Equal("LI", yards.TagName);
    }

    [Fact]
    public void The_bar_splits_at_the_shares_and_draws_a_bare_track_for_none()
    {
        var cut = Render(Rows);
        var rows = cut.FindAll(".teamcmp__row");

        Assert.Contains("flex-grow: 40.6", rows[1].QuerySelector(".teamcmp__seg--away")!.GetAttribute("style"));
        Assert.Contains("flex-grow: 59.4", rows[1].QuerySelector(".teamcmp__seg--home")!.GetAttribute("style"));

        Assert.Empty(rows[2].QuerySelectorAll(".teamcmp__seg"));
        Assert.Contains("teamcmp__bar--none", rows[2].QuerySelector(".teamcmp__bar")!.ClassName);
    }

    [Fact]
    public void No_rows_renders_nothing()
    {
        var cut = Render([]);

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void The_sheet_spends_no_state_colour_no_accent_and_no_fixed_width()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Snippets", "TeamComparison.razor.css"));
        var rules = XLines.Desk.Testing.DeskConventions.WithoutComments(css);

        Assert.DoesNotContain("border:", rules);
        Assert.DoesNotContain("--state-", rules);
        Assert.DoesNotContain("--accent", rules);
        Assert.DoesNotMatch(@"(?<![-\w])(max-)?width:\s*\d+px", rules);
    }
}
