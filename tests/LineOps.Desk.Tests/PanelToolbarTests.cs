using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Desk.Tests;

/// <summary>
/// The one row of controls that scope or act on a whole panel.
///
/// <para>
/// Pickers and search are the content; a message and the actions are slots. The actions
/// slot is where the panel's single Filled key lives in every window, pinned to the right
/// edge, and it is not drawn at all when a panel has nothing to put there — a visitor's Ops
/// window renders the pickers with no empty box after them.
/// </para>
/// </summary>
public class PanelToolbarTests : DeskTestContext
{
    [Fact]
    public void Omits_the_actions_slot_when_none_is_given()
    {
        var cut = RenderComponent<PanelToolbar>(p => p.AddChildContent("<select></select>"));

        Assert.Empty(cut.FindAll(".ptoolbar__actions"));
    }

    [Fact]
    public void Renders_actions_in_the_right_pinned_slot()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .Add(x => x.Actions, "<button>Sync</button>"));

        Assert.Equal("Sync", cut.Find(".ptoolbar__actions button").TextContent);
    }

    [Fact]
    public void Renders_the_message_between_content_and_actions()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .Add(x => x.Message, "Settled 3")
            .Add(x => x.Actions, "<button>Sync</button>"));

        var children = cut.Find(".ptoolbar").Children;

        Assert.Equal("SELECT", children[0].TagName);
        Assert.Contains("ptoolbar__msg", children[1].ClassList);
        Assert.Equal("Settled 3", children[1].TextContent);
        Assert.Contains("ptoolbar__actions", children[2].ClassList);
    }

    [Fact]
    public void Keeps_every_key_in_one_actions_item_after_the_pickers()
    {
        // A narrow window wraps the row: the keys must travel as one flex item, last in the
        // row, with the Filled key last inside it, or a key is left on a line of its own
        // beside a picker and the Filled one stops being the last thing on screen.
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select><select></select>")
            .Add(x => x.Actions, "<button>Refresh</button><button class=\"filled\">Pull data</button>"));

        var children = cut.Find(".ptoolbar").Children;
        var actions = children[^1];

        Assert.Contains("ptoolbar__actions", actions.ClassList);
        Assert.Single(cut.FindAll(".ptoolbar__actions"));
        Assert.Empty(cut.FindAll(".ptoolbar > button"));
        Assert.Equal(new[] { "Refresh", "Pull data" }, actions.Children.Select(c => c.TextContent));
        Assert.Contains("filled", actions.Children[^1].ClassList);
    }

    [Fact]
    public void Claims_no_aria_role()
    {
        // The ARIA toolbar pattern promises one tab stop with arrow keys between the
        // controls; these are ordinary controls, so the row must not announce otherwise.
        var cut = RenderComponent<PanelToolbar>(p => p.AddChildContent("<select></select>"));

        Assert.Null(cut.Find(".ptoolbar").GetAttribute("role"));
    }

    [Fact]
    public void Merges_a_callers_class_and_style()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select></select>")
            .AddUnmatched("class", "x")
            .AddUnmatched("style", "margin:0"));

        var root = cut.Find(".ptoolbar");

        Assert.Contains("x", root.ClassList);
        Assert.Contains("margin:0", root.GetAttribute("style"));
    }

    // ---- Overflow: a narrow toolbar keeps one line and folds its filters into More ----------

    private const string Kept = "<select class=\"kept\"></select>";

    private const string Folded =
        "<div class=\"ptoolbar__item\"><span class=\"ptoolbar__label\">Book</span><select class=\"folded\"></select></div>";

    private static AngleSharp.Dom.IElement? MoreKey(IRenderedComponent<PanelToolbar> cut)
        => cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == "More" || b.GetAttribute("aria-label") == "More");

    [Fact]
    public void Overflow_items_move_into_the_more_menu()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded)
            .Add(x => x.Actions, "<button>Sync</button>")
            .Add(x => x.ForceNarrow, true));

        // Folded away: not in the row, and the row says it is narrow.
        Assert.Equal("true", cut.Find(".ptoolbar").GetAttribute("data-narrow"));
        Assert.Empty(cut.FindAll(".folded"));
        Assert.Single(cut.FindAll(".ptoolbar > .kept"));

        var more = MoreKey(cut);
        Assert.NotNull(more);
        Assert.Equal("false", more!.GetAttribute("aria-expanded"));

        // The key sits straight after the filters still on the row, where the folded ones were;
        // the actions keep the trailing edge, the Filled key last.
        var children = cut.Find(".ptoolbar").Children.ToList();
        var kept = children.FindIndex(c => c.ClassList.Contains("kept"));
        Assert.Contains("ptoolbar__more-key", children[kept + 1].ClassList);
        Assert.Contains("ptoolbar__actions", children[^1].ClassList);

        more.Click();

        // The same control, in a labelled row of the popover panel — not a menu item.
        var row = cut.Find(".popover__panel .ptoolbar__more > .ptoolbar__item");
        Assert.Equal("Book", row.QuerySelector(".ptoolbar__label")!.TextContent);
        Assert.NotNull(row.QuerySelector("select.folded"));
        Assert.Empty(cut.FindAll(".ptoolbar__more [role=menu]"));
        Assert.Empty(cut.FindAll(".ptoolbar > .ptoolbar__item"));
        Assert.Equal("true", MoreKey(cut)!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Escape_in_the_more_panel_closes_it()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded)
            .Add(x => x.ForceNarrow, true));

        MoreKey(cut)!.Click();
        cut.Find(".ptoolbar__more").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".ptoolbar__more"));
        Assert.Equal("false", MoreKey(cut)!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Wide_toolbar_shows_overflow_inline()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded)
            .Add(x => x.Actions, "<button>Sync</button>"));

        Assert.Null(cut.Find(".ptoolbar").GetAttribute("data-narrow"));
        Assert.Single(cut.FindAll(".ptoolbar > .ptoolbar__item > select.folded"));
        Assert.Null(MoreKey(cut));

        // Inline, after the content and before the actions.
        var children = cut.Find(".ptoolbar").Children;
        Assert.Equal(new[] { "SELECT", "DIV", "DIV" }, children.Select(c => c.TagName));
        Assert.Contains("ptoolbar__actions", children[^1].ClassList);
    }

    [Fact]
    public void More_key_is_absent_when_nothing_overflows()
    {
        // A visitor's toolbar whose only overflow was a control that writes is handed no
        // Overflow at all, and a narrow row then draws no More key with nothing behind it.
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.ForceNarrow, true));

        Assert.Null(MoreKey(cut));
        Assert.Empty(cut.FindAll(".ptoolbar__more-key"));
    }

    [Fact]
    public void The_observer_folds_and_unfolds_the_row()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded));

        cut.InvokeAsync(() => cut.Instance.Fold(1));
        Assert.NotNull(MoreKey(cut));
        Assert.Empty(cut.FindAll(".folded"));

        cut.InvokeAsync(() => cut.Instance.Fold(0));
        Assert.Null(MoreKey(cut));
        Assert.Single(cut.FindAll(".ptoolbar > .ptoolbar__item > select.folded"));
    }

    [Fact]
    public void A_search_stays_in_the_row_until_the_second_fold()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded)
            .Add(x => x.Search, "<div class=\"search\"></div>"));

        // Wide: content, overflow, search — the search last in the content group, nearest the keys.
        var wide = cut.Find(".ptoolbar").Children;
        Assert.Contains("search", wide[^1].ClassList);
        Assert.Contains("ptoolbar__item", wide[^2].ClassList);

        cut.InvokeAsync(() => cut.Instance.Fold(1));
        Assert.Single(cut.FindAll(".ptoolbar > .search"));

        cut.InvokeAsync(() => cut.Instance.Fold(2));
        Assert.Empty(cut.FindAll(".ptoolbar > .search"));

        MoreKey(cut)!.Click();

        // Folded too, it leads the panel: the control most often reached for.
        Assert.Contains("search", cut.Find(".ptoolbar__more").Children[0].ClassList);
    }

    [Fact]
    public void Secondary_keys_fold_last_and_lead_the_more_panel()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.Overflow, Folded)
            .Add(x => x.SecondaryActions, "<button class=\"side\">Settle finished games</button><button class=\"side\">Build parlay</button>")
            .Add(x => x.Actions, "<button class=\"primary\">Log entry</button>"));

        // Wide: the side keys sit in the actions slot, before the primary.
        Assert.Equal(new[] { "Settle finished games", "Build parlay", "Log entry" },
            cut.FindAll(".ptoolbar__actions > button").Select(b => b.TextContent));

        // First fold: only the pickers go, and the More key still says its word.
        cut.InvokeAsync(() => cut.Instance.Fold(1));
        Assert.Equal(3, cut.FindAll(".ptoolbar__actions > button").Count);
        Assert.Equal("More", MoreKey(cut)!.TextContent.Trim());
        Assert.Null(MoreKey(cut)!.GetAttribute("aria-label"));

        // Second: the side keys follow, and the primary stays on the row, last.
        cut.InvokeAsync(() => cut.Instance.Fold(2));
        Assert.Equal(new[] { "Log entry" }, cut.FindAll(".ptoolbar__actions > button").Select(b => b.TextContent));
        Assert.Contains("ptoolbar__actions", cut.Find(".ptoolbar").Children[^1].ClassList);

        // Fully folded, the key itself is the last thing to give: the ellipsis alone, still named.
        var key = MoreKey(cut)!;
        Assert.Equal(string.Empty, key.TextContent.Trim());
        Assert.Equal("More", key.GetAttribute("aria-label"));
        Assert.Equal("More", key.GetAttribute("title"));

        MoreKey(cut)!.Click();

        var panel = cut.Find(".ptoolbar__more");
        Assert.Contains("ptoolbar__more-actions", panel.Children[0].ClassList);
        Assert.Equal(new[] { "Settle finished games", "Build parlay" },
            panel.Children[0].Children.Select(b => b.TextContent));
        Assert.Single(cut.FindAll(".ptoolbar__more select.folded"));
    }

    [Fact]
    public void Secondary_keys_fold_on_their_own_without_overflow()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent(Kept)
            .Add(x => x.SecondaryActions, "<button class=\"side\">Build parlay</button>")
            .Add(x => x.Actions, "<button class=\"primary\">Log entry</button>"));

        Assert.Null(MoreKey(cut));

        cut.InvokeAsync(() => cut.Instance.Fold(1));
        MoreKey(cut)!.Click();

        Assert.Single(cut.FindAll(".ptoolbar__more-actions > .side"));
        Assert.Empty(cut.FindAll(".ptoolbar__actions > .side"));
    }

    /// <summary>
    /// The DOM half of the fold cannot run here, so its two load-bearing choices are read from
    /// the source. A measure parked on requestAnimationFrame never ran in a hidden or throttled
    /// page, and blocked every later one: a Journal restored narrow on page load never folded
    /// until the viewport was resized.
    /// </summary>
    [Fact]
    public void Toolbar_script_measures_on_observe_without_waiting_for_a_frame()
    {
        var js = File.ReadAllText(Path.Combine(LineOps.Desk.Tests.Support.Repo.Root(),
            "src", "LineOps.Desk", "wwwroot", "js", "toolbar.js"));

        Assert.DoesNotContain("requestAnimationFrame(", js);
        Assert.Contains("queueMicrotask(() => measure(state))", js);

        // observe measures straight away, after the children are watched.
        Assert.Matches(@"rows\.set\(row, state\);\s*watchChildren\(state\);[\s\S]*?schedule\(state\);\s*return;", js);

        // A row with no width yet returns without folding, and is measured again on the
        // observer's next report — the row itself is observed.
        Assert.Matches(@"width === 0\) return;", js);
        Assert.Contains("state.resize.observe(state.row);", js);
        Assert.Contains("new ResizeObserver(() => schedule(state))", js);
    }
}
