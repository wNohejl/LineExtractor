using Bunit;
using XLines.Desk;
using XLines.Desk.Primitives;
using XLines.Ingestion.Services;
using XLines.Web.Components.Desk;
using XLines.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Board's toolbar: its three keys — Refresh, Pull data, and Date range with its popover —
/// and its value filter.
///
/// The value filter is an on/off, so it is a checkbox — not a key that swaps
/// between Plain and Tinted to say which state it is in. It reads and writes nothing, so a
/// visitor has it too (ADR 0018 hides controls that write).
/// </summary>
public class BoardToolbarTests : DeskTestContext
{
    [Fact]
    public void Ev_filter_is_a_checkbox()
    {
        bool? changed = null;

        var cut = RenderComponent<BoardValueFilter>(p => p
            .Add(x => x.Count, 3)
            .Add(x => x.Checked, false)
            .Add(x => x.CheckedChanged, (bool on) => changed = on));

        var box = cut.Find("label.checkbox input[type=checkbox]");

        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Only +EV (3)", cut.Find(".checkbox__label").TextContent.Trim());
        Assert.Empty(cut.FindAll("button"));

        box.Change(true);

        Assert.True(changed);
    }

    [Fact]
    public void Ev_filter_is_absent_when_no_game_has_value()
    {
        var cut = RenderComponent<BoardValueFilter>(p => p
            .Add(x => x.Count, 0)
            .Add(x => x.Checked, false));

        // Nothing at all, not an empty wrapper: an empty element in a toolbar is still a gap.
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    private static string BoardSource()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Panels", "BoardPanel.razor"));

    /// <summary>The slice of a source between a slot's opening and closing tags.</summary>
    internal static string Slot(string source, string name)
    {
        var start = source.IndexOf($"<{name}>", StringComparison.Ordinal);
        var end = source.IndexOf($"</{name}>", StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"No <{name}> slot");
        return source[start..end];
    }

    [Fact]
    public void Board_search_is_a_search_field()
    {
        var source = BoardSource();
        var search = Slot(source, "Search");

        Assert.Contains("<DeskSearch", search);
        Assert.Contains("Placeholder=\"Search teams\"", search);
        Assert.DoesNotContain("placeholder=\"Team\"", source);
    }

    [Fact]
    public void The_board_s_keys_are_refresh_pull_data_and_date_range_in_that_order()
    {
        var actions = Slot(BoardSource(), "Actions");

        var refresh = actions.IndexOf("Title=\"Refresh\"", StringComparison.Ordinal);
        var pull = actions.IndexOf("<DeskJobMenu", StringComparison.Ordinal);
        var range = actions.IndexOf("<RangePullKey", StringComparison.Ordinal);

        Assert.True(refresh >= 0 && pull > refresh && range > pull, $"Refresh {refresh}, Pull data {pull}, Date range {range}");
        Assert.Contains("Label=\"Pull data\"", actions);

        // The key spends, so only an operator's toolbar draws it, as the panel's other operator controls check.
        var gate = actions.LastIndexOf("@if (Manager.Access.CanOperate)", range, StringComparison.Ordinal);
        Assert.True(gate > pull, "Date range is drawn only for an operator");

        // The range is no longer at the foot of the menu.
        Assert.DoesNotContain("RangePullSheet", BoardSource());
    }

    // ---- The Date range key ------------------------------------------------------------------

    private readonly List<RangePullRequest> _pulled = [];
    private TaskCompletionSource<RangePullSummary>? _running;
    private IProgress<RangePullProgress>? _progress;

    private IRenderedComponent<RangePullKey> Key(bool visitor = false)
    {
        Services.AddLogging();
        Services.AddScoped<DeskToasts>();
        Services.AddScoped<DeskJobRunner>();

        if (visitor)
            Services.AddSingleton<IDeskAccess>(new Visitor());

        return RenderComponent<RangePullKey>(p => p
            .Add(x => x.Leagues, _ => Task.FromResult<IReadOnlyList<RangeLeague>>([new("mlb", "MLB"), new("nfl", "NFL")]))
            .Add(x => x.Pull, (r, progress, _) =>
            {
                _pulled.Add(r);
                _progress = progress;
                _running = new TaskCompletionSource<RangePullSummary>();
                return _running.Task;
            })
            .Add(x => x.InitialLeague, "nfl")
            .Add(x => x.Today, new DateOnly(2026, 11, 7)));
    }

    private static AngleSharp.Dom.IElement DateRange(IRenderedFragment cut)
        => cut.Find("button[aria-haspopup=dialog]");

    [Fact]
    public void Date_range_is_a_tinted_key_that_says_it_opens_a_dialog()
    {
        var cut = Key();
        var key = DateRange(cut);

        Assert.Equal("Date range", key.TextContent.Trim());
        Assert.Contains("desk-btn--tinted", key.ClassList);
        Assert.Equal("true", key.GetAttribute("data-popover-return"));
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".range-pop"));
    }

    [Fact]
    public void Date_range_opens_a_popover_holding_the_form()
    {
        var cut = Key();

        DateRange(cut).Click();

        var panel = cut.Find(".popover__panel");
        Assert.NotNull(panel.QuerySelector(".range-pop[role=dialog]"));
        Assert.Equal("true", DateRange(cut).GetAttribute("aria-expanded"));

        // The Board's sport is the league it starts on.
        Assert.Equal("true", cut.FindAll("button[role=radio]").Single(b => b.TextContent.Trim() == "NFL").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Pull_closes_the_popover_and_runs_on_the_busy_key_with_the_day_it_has_reached()
    {
        var cut = Key();
        var toasts = Services.GetRequiredService<DeskToasts>();

        DateRange(cut).Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Pull").Click();

        Assert.Empty(cut.FindAll(".range-pop"));
        Assert.Equal("nfl", Assert.Single(_pulled).Sport);
        Assert.Equal("true", DateRange(cut).GetAttribute("aria-busy"));

        _progress!.Report(new RangePullProgress("nfl", new DateOnly(2026, 11, 2), 2, 4, 2, 0));
        // The word on the key is the day reached; its name keeps "Date range" in front of it, read
        // but not seen, so the words on the key are in its name and a reader still knows the key.
        cut.WaitForAssertion(() => Assert.Equal("NFL Nov 2 · 3 of 4", DateRange(cut).QuerySelector(".range-key__word")!.TextContent.Trim()));
        Assert.Equal("Date range · NFL Nov 2 · 3 of 4", System.Text.RegularExpressions.Regex.Replace(DateRange(cut).TextContent, @"\s+", " ").Trim());
        Assert.False(DateRange(cut).HasAttribute("disabled"));   // busy, but it keeps its focus

        _running!.SetResult(new RangePullSummary("nfl", new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 6),
            7, 4, true, 4, 0, 16, 16, 700, 64, 24, 0, null));

        cut.WaitForAssertion(() => Assert.Equal("Date range", DateRange(cut).TextContent.Trim()));
        Assert.Null(DateRange(cut).GetAttribute("aria-busy"));
        Assert.NotNull(toasts.Last);
    }

    [Fact]
    public void A_visitor_sees_neither_pull_data_nor_date_range()
    {
        Services.AddScoped<IDeskJobs, NoJobs>();
        var cut = Key(visitor: true);
        var menu = RenderComponent<DeskJobMenu>(p => p.Add(x => x.Label, "Pull data"));

        Assert.Equal(string.Empty, cut.Markup.Trim());
        Assert.Equal(string.Empty, menu.Markup.Trim());
    }

    private sealed class Visitor : IDeskAccess
    {
        public bool CanOperate => false;
    }

    private sealed class NoJobs : IDeskJobs
    {
        public Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeskJob>>([new("x", "X", "x", null, true, null)]);

        public Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
            => Task.FromResult(new DeskJobOutcome(DeskJobResult.Nothing, "x"));
    }

    [Fact]
    public void Board_filters_live_in_overflow()
    {
        // At a narrow width the scope pickers and the value filter fold into More; the search
        // and the keys stay on the row until the row is narrower still.
        var source = BoardSource();
        var overflow = Slot(source, "Overflow");

        Assert.Contains("_sportOptions", overflow);
        Assert.Contains("Options=\"Horizons\"", overflow);
        Assert.Contains("Options=\"_phaseOptions\"", overflow);
        Assert.Contains("<BoardValueFilter", overflow);
        Assert.DoesNotContain("<DeskSearch", overflow);
        Assert.DoesNotContain("DeskJobMenu", overflow);

        // Each folded control carries the label it shows as a row of the More panel.
        Assert.Contains("class=\"ptoolbar__label\" aria-hidden=\"true\">Sport<", overflow);
    }
}
