using Bunit;
using XLines.Ingestion.Services;
using XLines.Web.Components.Desk;

namespace XLines.Web.Tests;

/// <summary>
/// The Board's date-range popover: Pull waits, with its reason on the key row, until the answers
/// are a range a pull takes — a league named and From not after To; the quick ranges set the
/// dates; Custom shows them; and Pull hands the answers back.
/// </summary>
public class RangePullPopoverTests : DeskTestContext
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    private static readonly RangeLeague Nfl = new("nfl", "NFL");
    private static readonly RangeLeague Mlb = new("mlb", "MLB");

    private readonly List<RangePullRequest> _pulled = [];
    private int _cancelled;

    private IRenderedComponent<RangePullPopover> Open(IReadOnlyList<RangeLeague> leagues, string? initial = null)
        => RenderComponent<RangePullPopover>(p => p
            .Add(x => x.Leagues, leagues)
            .Add(x => x.InitialLeague, initial)
            .Add(x => x.Today, Today)
            .Add(x => x.OnPull, (RangePullRequest r) => _pulled.Add(r))
            .Add(x => x.OnCancel, () => _cancelled++));

    private static AngleSharp.Dom.IElement Key(IRenderedFragment cut, string label)
        => cut.FindAll("button").Single(b => b.TextContent.Trim() == label);

    private static AngleSharp.Dom.IElement Segment(IRenderedFragment cut, string label)
        => cut.FindAll("button[role=radio]").Single(b => b.TextContent.Trim() == label);

    private static string? Reason(IRenderedFragment cut)
        => cut.FindAll(".keyrow__reason").SingleOrDefault()?.TextContent;

    private static IReadOnlyList<AngleSharp.Dom.IElement> Dates(IRenderedFragment cut)
        => cut.FindAll("input[type=date]");

    [Fact]
    public void A_range_ready_to_pull_has_no_reason_and_an_enabled_pull()
    {
        var cut = Open([Nfl]);

        Assert.Null(Reason(cut));
        Assert.False(Key(cut, "Pull").HasAttribute("disabled"));
        Assert.Contains("7 days, Oct 31 to Nov 6.", cut.Find(".wgroup__foot").TextContent);
    }

    [Fact]
    public void From_after_To_waits_and_says_why()
    {
        var cut = Open([Nfl]);

        Segment(cut, "Custom").Click();
        Dates(cut)[0].Input("2024-09-30");
        Dates(cut)[1].Input("2024-09-01");

        Assert.Equal("From (2024-09-30) is after To (2024-09-01).", Reason(cut));
        var pull = Key(cut, "Pull");
        Assert.True(pull.HasAttribute("disabled"));
        Assert.Equal(cut.Find(".keyrow__reason").Id, pull.GetAttribute("aria-describedby"));

        // The reason is said once, on the key row; the footer's count stands down while it waits.
        Assert.Empty(cut.FindAll(".wgroup__foot"));
    }

    [Fact]
    public void No_league_waits_and_says_why()
    {
        var cut = Open([]);

        Assert.Equal("Name a league.", Reason(cut));
        Assert.True(Key(cut, "Pull").HasAttribute("disabled"));
    }

    [Fact]
    public void Last_7_days_is_the_week_before_today()
    {
        var cut = Open([Nfl]);

        Key(cut, "Pull").Click();

        var asked = Assert.Single(_pulled);
        Assert.Equal(Today.AddDays(-7), asked.From);
        Assert.Equal(Today.AddDays(-1), asked.To);
    }

    [Fact]
    public void Last_30_days_sets_the_dates_Custom_then_shows()
    {
        var cut = Open([Nfl]);

        Segment(cut, "30 days").Click();
        Segment(cut, "Custom").Click();

        Assert.Equal(["2026-10-08", "2026-11-06"], Dates(cut).Select(d => d.GetAttribute("value")));
    }

    [Fact]
    public void Custom_reveals_both_fields()
    {
        var cut = Open([Nfl]);

        Assert.Empty(Dates(cut));

        Segment(cut, "Custom").Click();

        Assert.Equal(2, Dates(cut).Count);
        Assert.Equal(["From", "To"], cut.FindAll(".range-pop__dates label").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void A_few_leagues_are_a_segmented_control_and_the_board_s_sport_is_chosen()
    {
        var cut = Open([Mlb, Nfl], initial: "NFL");

        Assert.Empty(cut.FindAll("select"));
        Assert.Equal("true", Segment(cut, "NFL").GetAttribute("aria-checked"));
    }

    [Fact]
    public void More_than_five_leagues_are_a_pop_up()
    {
        var many = Enumerable.Range(1, 6).Select(i => new RangeLeague($"l{i}", $"League {i}")).ToList();

        var cut = Open(many);

        Assert.Equal(6, cut.FindAll("select[aria-label=League] option").Count);
    }

    [Fact]
    public void Pull_raises_the_request_for_the_answers_given()
    {
        var cut = Open([Mlb, Nfl]);

        Segment(cut, "MLB").Click();
        Segment(cut, "Custom").Click();
        Dates(cut)[0].Input("2025-04-01");
        Dates(cut)[1].Input("2025-04-30");
        cut.Find("input[role=switch]").Change(true);
        Key(cut, "Pull").Click();

        Assert.Equal(new RangePullRequest("mlb", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), true),
            Assert.Single(_pulled));
    }

    [Fact]
    public void Enter_in_a_date_presses_pull()
    {
        var cut = Open([Nfl]);

        Segment(cut, "Custom").Click();
        Dates(cut)[1].KeyDown("Enter");

        Assert.Single(_pulled);
    }

    /// <summary>A date is committed as it is typed, so Enter pulls the date on screen, not the one before it.</summary>
    [Fact]
    public void Enter_pulls_the_date_just_typed()
    {
        var cut = Open([Nfl]);

        Segment(cut, "Custom").Click();
        Dates(cut)[0].Input("2024-09-01");
        Dates(cut)[1].Input("2024-09-09");
        Dates(cut)[1].KeyDown("Enter");

        var asked = Assert.Single(_pulled);
        Assert.Equal((new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 9)), (asked.From, asked.To));
    }

    [Fact]
    public void Enter_does_not_pull_a_range_that_waits()
    {
        var cut = Open([Nfl]);

        Segment(cut, "Custom").Click();
        Dates(cut)[0].Input("2024-09-30");
        Dates(cut)[1].Input("2024-09-01");
        Dates(cut)[1].KeyDown("Enter");

        Assert.Empty(_pulled);
    }

    [Fact]
    public void Cancel_raises_cancel_and_pulls_nothing()
    {
        var cut = Open([Nfl]);

        Key(cut, "Cancel").Click();

        Assert.Equal(1, _cancelled);
        Assert.Empty(_pulled);
    }

    /// <summary>A form's fields do not close a popover on their own, as a menu's rows do: Escape anywhere in it is Cancel.</summary>
    [Fact]
    public void Escape_anywhere_in_the_form_is_cancel()
    {
        var cut = Open([Nfl]);

        cut.Find("[role=dialog]").KeyDown("Escape");

        Assert.Equal(1, _cancelled);
        Assert.Empty(_pulled);
    }

    [Fact]
    public void Pull_is_the_one_filled_key()
        => Assert.Single(Open([Nfl]).FindAll(".desk-btn--filled"));
}
