using System.Globalization;
using System.Reflection;
using Bunit;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Ingestion.Services;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Performance window's words and figures. Its tables read in display names — "Spread",
/// "DraftKings" — under a first column named for what the rows are, and its money is signed with
/// a real minus rather than the regional format's accountant's parentheses. Read from the source
/// where the window needs a database to render; the bankroll row is rendered, with what a load
/// would have read placed on the panel and no JournalService behind it, so a write throws.
/// </summary>
public class PerformancePanelTests : DeskTestContext
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

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "wwwroot", "css", "lineops.css"));
        Assert.Contains(".perf-chart .mud-chart svg.mud-chart-line { display: block; width: 100%; height: auto; margin: 0; }", css);
        Assert.Contains(".perf-chart .mud-chart text { font-size: var(--text-footnote); fill: var(--text-secondary); }", css);
    }

    private static void Set(object panel, string field, object? value)
    {
        for (var type = panel.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) is { } f)
            {
                f.SetValue(panel, value);
                return;
            }
        }

        throw new InvalidOperationException($"No field {field} on {panel.GetType().Name}.");
    }

    /// <summary>
    /// The window with two settled bets read: the load itself fails (nothing serves the journal
    /// here), and the test places what it would have read and marks the panel loaded.
    /// </summary>
    private IRenderedComponent<PerformancePanel> RenderSettled()
    {
        var panel = RenderComponent<PerformancePanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_anySettled", true);
        Set(panel.Instance, "_summary", new PerformanceSummary(2, 1, 1, 0, 200m, 10m));
        typeof(LineOps.Desk.Windowing.DeskPanelBase).GetProperty("LoadError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel.Instance, null);
        typeof(LineOps.Desk.Windowing.DeskPanelBase).GetProperty("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel.Instance, true);
        panel.Render();
        return panel;
    }

    /// <summary>
    /// The starting bankroll is a setting, so it is a row of a group: its name leads, the field
    /// and its key trail, and what the figure is for is the group's footer. It sat on the
    /// section's rule beside a label hidden from the screen reader, where a section's keys narrow
    /// what it shows and do not store anything.
    /// </summary>
    [Fact]
    public void Bankroll_is_a_setting_row()
    {
        var panel = RenderSettled();

        var section = panel.Find("section.wsection[aria-label='Bankroll']");
        var row = section.QuerySelector(".wgroup__row--form")!;

        Assert.Equal("Starting bankroll", row.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.Equal("Starting bankroll", row.QuerySelector("input")!.GetAttribute("aria-label"));
        Assert.Contains("The curve starts here", section.QuerySelector(".wgroup__foot")!.TextContent);

        var set = row.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Set");
        Assert.DoesNotContain("desk-btn--filled", set.ClassName);

        Assert.Empty(panel.FindAll(".psection__keys .perf-bankroll"));
    }

    /// <summary>A figure the journal would not store is said under the row, and stays in the field.</summary>
    [Fact]
    public void A_bankroll_that_fails_to_save_says_so()
    {
        var panel = RenderSettled();

        panel.Find("input[aria-label='Starting bankroll']").Change("500");
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Set").Click();

        panel.WaitForAssertion(() =>
            Assert.Contains("The bankroll was not saved. Try again.",
                panel.Find("section.wsection[aria-label='Bankroll'] .note").TextContent));
        Assert.Equal("500", panel.Find("input[aria-label='Starting bankroll']").GetAttribute("value"));
    }

    /// <summary>
    /// A journal held in memory that counts what Performance asks of it. Built on no database:
    /// the two reads the window makes are the ones overridden here, and anything else throws.
    /// </summary>
    private sealed class CountedJournal() : JournalService(null!, null!)
    {
        public List<PerformanceFilter> Reads { get; } = [];

        public List<JournalEntry> Entries { get; } = [];

        public override Task<PerformanceSet> PerformanceAsync(PerformanceFilter filter, CancellationToken ct = default)
        {
            Reads.Add(filter);
            var whole = new PerformanceSet(Entries.ToList(), Entries.Where(e => e.IsGraded).ToList());
            return Task.FromResult(Narrow(whole, filter));
        }

        public override Task<decimal> StartingBankrollAsync(CancellationToken ct = default) => Task.FromResult(0m);
    }

    private static JournalEntry Won(string sport, string book) => new()
    {
        Game = new Game { Sport = new Sport { Key = sport, Name = sport.ToUpperInvariant() }, SeasonYear = 2026 },
        Market = "h2h",
        Outcome = "Home",
        Book = book,
        Stake = 100m,
        PriceTaken = 100,
        Result = EntryResult.Win,
        Payout = 200m,
        PlacedAt = DateTimeOffset.UtcNow.AddDays(-2),
        SettledAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private CountedJournal UseJournal()
    {
        var journal = new CountedJournal();
        journal.Entries.Add(Won("mlb", "draftkings"));
        journal.Entries.Add(Won("nfl", "fanduel"));
        Services.AddSingleton<JournalService>(journal);
        return journal;
    }

    /// <summary>
    /// One load is one read of the journal. It was two: the whole for the pickers, then the
    /// filtered set for the figures — a second trip for a subset of what the first brought back.
    /// </summary>
    [Fact]
    public void A_load_reads_the_journal_once()
    {
        var journal = UseJournal();

        var panel = RenderComponent<PerformancePanel>();
        panel.WaitForAssertion(() => Assert.Contains("2 bets", panel.Markup));

        Assert.Equal([new PerformanceFilter()], journal.Reads);
    }

    /// <summary>
    /// A filter narrows the figures from the one read, and the pickers keep coming from the
    /// whole, so choosing MLB still leaves NFL on the Sport switch to go back to.
    /// </summary>
    [Fact]
    public void A_filter_narrows_the_one_read_and_keeps_the_way_back()
    {
        var journal = UseJournal();

        var panel = RenderComponent<PerformancePanel>();
        panel.WaitForAssertion(() => Assert.Contains("2 bets", panel.Markup));

        panel.FindAll("[role=radiogroup][aria-label=Sport] [role=radio]")
            .Single(b => b.TextContent.Trim() == "MLB").Click();

        panel.WaitForAssertion(() => Assert.Contains("1 bet", panel.Markup));
        Assert.DoesNotContain("2 bets", panel.Markup);
        Assert.Equal([new PerformanceFilter(), new PerformanceFilter()], journal.Reads);

        var sports = panel.FindAll("[role=radiogroup][aria-label=Sport] [role=radio]")
            .Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["All", "MLB", "NFL"], sports);
    }
}
