using System.Globalization;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Performance window's words and figures. Its tables read in display names — "Spread",
/// "DraftKings" — under a first column named for what the rows are, and its money is signed with
/// a real minus rather than the regional format's accountant's parentheses. Read from the source
/// where the window needs a database to render.
/// </summary>
public class PerformancePanelTests
{
    private static string Source() => File.ReadAllText(Path.Combine(
        Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "PerformancePanel.razor"));

    /// <summary>A loss is "−$20", never "($20)", whatever the machine's regional format says.</summary>
    [Fact]
    public void Money_carries_its_sign_and_its_thousands()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            Assert.Equal("+$1,240", PerformancePanel.Signed(1240m));
            Assert.Equal("−$20", PerformancePanel.Signed(-20m));
            Assert.Equal("$0", PerformancePanel.Signed(0m));
            Assert.Equal("$0", PerformancePanel.Signed(-0.2m));

            Assert.Equal("$12,500", PerformancePanel.Money(12500m));
            Assert.Equal("−$20", PerformancePanel.Money(-20m));

            Assert.Equal("+12.3%", PerformancePanel.Percent(0.123m));
            Assert.Equal("−4.0%", PerformancePanel.Percent(-0.04m));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("h2h", "ML")]
    [InlineData("spread", "Spread")]
    [InlineData("total", "Total")]
    [InlineData("parlay", "Parlay")]
    [InlineData("other", "Other")]
    public void Markets_read_by_their_display_names(string key, string shown)
        => Assert.Equal(shown, PerformancePanel.MarketLabel(key));

    /// <summary>Books go by the desk's one map of names, in the tables and the Book picker alike.</summary>
    [Fact]
    public void Books_read_by_the_desks_one_map_of_names()
    {
        var source = Source();

        Assert.Contains("e => BookNames.Display(e.Book)", source);
        Assert.Contains("@BookNames.Display(book)", source);
        Assert.DoesNotContain("BookName(", source);
    }

    /// <summary>
    /// The three breakdowns name their first column for what the rows are, not "Key", and share
    /// one set of column classes so their figures line up down the page.
    /// </summary>
    [Fact]
    public void Breakdowns_name_their_first_column_and_share_widths()
    {
        var source = Source();

        Assert.DoesNotContain("Title=\"Key\"", source);
        Assert.Contains("@Breakdown(_byMarket, \"Market\")", source);
        Assert.Contains("@Breakdown(_bySport, \"Sport\")", source);
        Assert.Contains("@Breakdown(_byBook, \"Book\")", source);
        Assert.Contains("Property=\"r => r.Label\" Title=\"@by\"", source);
        Assert.Contains("Class=\"perf-table\"", source);

        // No figure goes through C0, which follows the regional format.
        Assert.DoesNotContain(".ToString(\"C0\")", source);
    }

    /// <summary>The chart spans the column at its own proportions, and its summary keeps the span.</summary>
    [Fact]
    public void The_bankroll_chart_spans_its_column()
    {
        var source = Source();

        Assert.Contains("Height=\"auto\"", source);
        Assert.Contains("Class=\"perf-chart\"", source);
        Assert.Contains("Takeaway=\"@_takeaway\"", source);

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "lineops.css"));
        Assert.Contains(".perf-chart .mud-chart svg.mud-chart-line { display: block; width: 100%; height: auto; margin: 0; }", css);
        Assert.Contains(".perf-chart .mud-chart text { font-size: var(--text-footnote); fill: var(--text-secondary); }", css);
    }
}
