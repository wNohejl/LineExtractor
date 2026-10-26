using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// The action strip on an open row. Two-stage disclosure: a key answers in place if it can,
/// and only spends a window if the snippet is not enough.
/// </summary>
public class RowActionsTests : DeskTestContext
{
    private static RenderFragment Markup(string text)
        => b => b.AddMarkupContent(0, $"<p>{text}</p>");

    private static RowAction Snippet(string key, string label, string? openLabel = null, Func<Task>? open = null)
        => new()
        {
            Key = key,
            Label = label,
            Icon = MudBlazor.Icons.Material.Filled.History,
            Snippet = Markup($"{key} snippet"),
            OpenLabel = openLabel,
            OpenWindow = open
        };

    private static RowAction Destination(string key, string label, Func<Task> open)
        => new()
        {
            Key = key,
            Label = label,
            Icon = MudBlazor.Icons.Material.Filled.OpenInNew,
            OpenWindow = open
        };

    private IRenderedComponent<RowActions> Strip(IReadOnlyList<RowAction> actions)
        => RenderComponent<RowActions>(p => p.Add(x => x.Actions, actions));

    [Fact]
    public void Renders_a_key_per_action()
    {
        var cut = Strip([Snippet("h2h", "Head to head"), Snippet("last5", "Last five")]);

        Assert.Equal(["Head to head", "Last five"],
            cut.FindAll(".rowacts__keys button").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void Nothing_is_open_to_begin_with()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        Assert.Empty(cut.FindAll("[role=region]"));
        Assert.Equal("false", cut.Find(".rowacts__keys button").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Pressing_a_snippet_key_opens_it_in_place()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();

        Assert.Contains("h2h snippet", cut.Find("[role=region]").TextContent);
    }

    /// <summary>
    /// A snippet key is a toggle and reports pressed state; the key also points at the region
    /// it controls, so the pair reads as one thing.
    /// </summary>
    [Fact]
    public void An_open_snippet_key_reports_pressed_expanded_and_what_it_controls()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();

        var key = cut.Find(".rowacts__keys button");

        Assert.Equal("true", key.GetAttribute("aria-pressed"));
        Assert.Equal("true", key.GetAttribute("aria-expanded"));
        Assert.Equal(cut.Find("[role=region]").GetAttribute("id"), key.GetAttribute("aria-controls"));
        Assert.Contains("desk-btn--on", key.GetAttribute("class"));
    }

    /// <summary>
    /// A key that opens a window is an ordinary command. Claiming a pressed state would have a
    /// screen reader announce a toggle that never changes.
    /// </summary>
    [Fact]
    public void A_window_only_key_does_not_claim_to_be_a_toggle()
    {
        var cut = Strip([Destination("team", "Team", () => Task.CompletedTask)]);

        var key = cut.Find(".rowacts__keys button");

        Assert.Null(key.GetAttribute("aria-pressed"));
        Assert.Null(key.GetAttribute("aria-expanded"));
        Assert.Null(key.GetAttribute("aria-controls"));
    }

    [Fact]
    public void A_window_only_key_goes_straight_there()
    {
        var opened = 0;

        var cut = Strip([Destination("team", "Team", () => { opened++; return Task.CompletedTask; })]);

        cut.Find(".rowacts__keys button").Click();

        Assert.Equal(1, opened);
        Assert.Empty(cut.FindAll("[role=region]"));
    }

    /// <summary>
    /// A lone view is a disclosure, not a choice — a one-segment control has nothing to choose
    /// between — so it stays a toggle key, and pressing it again returns the row to its keys.
    /// </summary>
    [Fact]
    public void Pressing_the_open_key_again_closes_it()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();
        cut.Find(".rowacts__keys button").Click();

        Assert.Empty(cut.FindAll("[role=region]"));
        Assert.Equal("false", cut.Find(".rowacts__keys button").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// One snippet at a time. A row that grows without bound pushes the rest of the slate off
    /// screen and the snippet stops reading as attached to its game.
    /// </summary>
    [Fact]
    public void Selecting_another_snippet_swaps_it_rather_than_stacking()
    {
        var cut = Strip([Snippet("h2h", "Head to head"), Snippet("last5", "Last five")]);

        cut.FindAll(".rowacts__keys [role=radio]")[0].Click();
        cut.FindAll(".rowacts__keys [role=radio]")[1].Click();

        var regions = cut.FindAll("[role=region]");

        Assert.Single(regions);
        Assert.Contains("last5 snippet", regions[0].TextContent);

        var views = cut.FindAll(".rowacts__keys [role=radio]");
        Assert.Equal("false", views[0].GetAttribute("aria-checked"));
        Assert.Equal("true", views[1].GetAttribute("aria-checked"));
    }

    /// <summary>
    /// Several views are one choice — which answer is showing — so they are one segmented
    /// control, and choosing a segment opens that snippet. The keys that open a window are
    /// commands, not choices, and sit after it rather than inside it (HIG: a segmented control
    /// never mixes selection and action).
    /// </summary>
    [Fact]
    public void Selecting_a_view_opens_that_snippet()
    {
        var opened = 0;

        var cut = Strip([
            Snippet("h2h", "Head to head"),
            Snippet("last5", "Last five"),
            Destination("team", "Team", () => { opened++; return Task.CompletedTask; })
        ]);

        var group = cut.Find(".rowacts__keys [role=radiogroup]");
        Assert.Equal(["Head to head", "Last five"],
            group.QuerySelectorAll("[role=radio]").Select(b => b.TextContent.Trim()));
        Assert.All(group.QuerySelectorAll("[role=radio]"),
            v => Assert.Equal("false", v.GetAttribute("aria-checked")));

        cut.FindAll(".rowacts__keys [role=radio]")[1].Click();

        Assert.Contains("last5 snippet", cut.Find("[role=region]").TextContent);
        Assert.Equal("true", cut.FindAll(".rowacts__keys [role=radio]")[1].GetAttribute("aria-checked"));
        Assert.Equal(cut.Find("[role=region]").GetAttribute("id"),
            cut.Find(".rowacts__keys [role=radiogroup]").GetAttribute("aria-controls"));

        // Choosing a view spends no window.
        Assert.Equal(0, opened);
    }

    /// <summary>
    /// The split must not strand a key. The views are one Tab stop with arrows inside it (a
    /// radiogroup's roving tabindex), and every action key after them is its own Tab stop.
    /// </summary>
    [Fact]
    public void Row_actions_keep_every_key_focusable()
    {
        var cut = Strip([
            Snippet("odds", "Odds"),
            Snippet("h2h", "Head to head"),
            Destination("bets", "More bets", () => Task.CompletedTask),
            Destination("wager", "Place wager", () => Task.CompletedTask)
        ]);

        var views = cut.FindAll(".rowacts__keys [role=radio]");
        Assert.Equal(2, views.Count);
        Assert.Single(views, v => v.GetAttribute("tabindex") == "0");
        Assert.All(views, v => Assert.Equal("BUTTON", v.TagName));

        var commands = cut.FindAll(".rowacts__keys button:not([role=radio])");
        Assert.Equal(["More bets", "Place wager"], commands.Select(b => b.TextContent.Trim()));
        Assert.All(commands, b =>
        {
            Assert.NotEqual("-1", b.GetAttribute("tabindex"));
            Assert.False(b.HasAttribute("disabled"));
            Assert.Null(b.GetAttribute("aria-pressed"));
        });

        // Arrows move through the views from the group's one stop.
        cut.Find(".rowacts__keys [role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Contains("h2h snippet", cut.Find("[role=region]").TextContent);
    }

    /// <summary>
    /// A chosen segment stays chosen when pressed again — it is a radio, not a toggle. The way
    /// back to the bare keys is Escape, as for everything else that opens on this desk.
    /// </summary>
    [Fact]
    public void Pressing_the_chosen_view_again_keeps_it_open()
    {
        var cut = Strip([Snippet("h2h", "Head to head"), Snippet("last5", "Last five")]);

        cut.FindAll(".rowacts__keys [role=radio]")[0].Click();
        cut.FindAll(".rowacts__keys [role=radio]")[0].Click();

        Assert.Contains("h2h snippet", cut.Find("[role=region]").TextContent);

        cut.Find(".rowacts").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=region]"));
        Assert.All(cut.FindAll(".rowacts__keys [role=radio]"),
            v => Assert.Equal("false", v.GetAttribute("aria-checked")));
    }

    /// <summary>Every other thing you can open on this desk closes with Escape.</summary>
    [Fact]
    public void Escape_returns_the_row_to_its_keys()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();
        cut.Find(".rowacts").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=region]"));
    }

    [Fact]
    public void Another_key_leaves_the_snippet_open()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();
        cut.Find(".rowacts").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Single(cut.FindAll("[role=region]"));
    }

    /// <summary>The snippet is a named landmark, so its arrival is announced and identified.</summary>
    [Fact]
    public void The_snippet_is_a_region_named_by_its_action()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();

        Assert.Equal("Head to head", cut.Find("[role=region]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_snippet_that_is_complete_on_the_row_offers_no_escape()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();

        Assert.Empty(cut.FindAll(".snippet__open"));
    }

    [Fact]
    public void A_snippet_with_more_behind_it_offers_the_way_out()
    {
        var opened = 0;

        var cut = Strip([Snippet("h2h", "Head to head", "Full history",
            () => { opened++; return Task.CompletedTask; })]);

        cut.Find(".rowacts__keys button").Click();

        var escape = cut.Find(".snippet__open");
        Assert.Contains("Full history", escape.TextContent);

        escape.Click();
        Assert.Equal(1, opened);
    }

    /// <summary>
    /// An action states how much weight it claims and whether it destroys something, and the
    /// strip forwards both to the key it renders. They are two independent questions — a
    /// destructive action can be any weight — so a strip that dropped either one would render
    /// a key that lies about what pressing it does.
    /// </summary>
    [Fact]
    public void A_keys_weight_and_role_come_from_its_action()
    {
        var plain = Destination("h2h", "Head to head", () => Task.CompletedTask);

        var cut = Strip([
            plain,
            plain with { Key = "wager", Label = "Place wager", Emphasis = DeskEmphasis.Tinted },
            plain with { Key = "void", Label = "Void entry", Role = DeskRole.Destructive }
        ]);

        var keys = cut.FindAll(".rowacts__keys button");

        // Nothing stated: the quiet default, so it cannot compete with the key beside it.
        Assert.Contains("desk-btn--plain", keys[0].GetAttribute("class"));
        Assert.DoesNotContain("desk-btn--destructive", keys[0].GetAttribute("class"));

        Assert.Contains("desk-btn--tinted", keys[1].GetAttribute("class"));
        Assert.DoesNotContain("desk-btn--destructive", keys[1].GetAttribute("class"));

        // Weight and role are carried separately: a Plain key can still be destructive.
        Assert.Contains("desk-btn--plain", keys[2].GetAttribute("class"));
        Assert.Contains("desk-btn--destructive", keys[2].GetAttribute("class"));
    }

    /// <summary>Several strips share a page — the Team window has a stack of them.</summary>
    [Fact]
    public void Two_strips_on_one_page_do_not_share_a_snippet_id()
    {
        var first = Strip([Snippet("h2h", "Head to head")]);
        var second = Strip([Snippet("h2h", "Head to head")]);

        first.Find(".rowacts__keys button").Click();
        second.Find(".rowacts__keys button").Click();

        Assert.NotEqual(
            first.Find("[role=region]").GetAttribute("id"),
            second.Find("[role=region]").GetAttribute("id"));
    }

    /// <summary>
    /// The Game window's player rows give no host: the snippet opens inside the strip, as it
    /// always has.
    /// </summary>
    [Fact]
    public void Without_a_host_the_snippet_opens_in_the_strip()
    {
        var cut = Strip([Snippet("h2h", "Head to head")]);

        cut.Find(".rowacts__keys button").Click();

        Assert.Contains("h2h snippet", cut.Find(".rowacts [role=region]").TextContent);
        Assert.Empty(cut.FindAll(".rowacts__snippet"));
    }

    private IRenderedComponent<RowActions> HostedStrip(IReadOnlyList<RowAction> actions, List<RenderFragment?> handed)
        => RenderComponent<RowActions>(p => p
            .Add(x => x.Actions, actions)
            .Add(x => x.SnippetHost, (RenderFragment? f) => handed.Add(f)));

    /// <summary>
    /// The Board hosts the snippet in a row of its own under the game, so it spans the row
    /// instead of wrapping every team name inside the matchup column. The keys stay put.
    /// </summary>
    [Fact]
    public void With_a_host_the_snippet_is_handed_out_rather_than_drawn_in_the_strip()
    {
        var handed = new List<RenderFragment?>();
        var cut = HostedStrip([Snippet("odds", "Odds"), Snippet("h2h", "Head to head")], handed);

        cut.FindAll(".rowacts__keys [role=radio]")[1].Click();

        Assert.Empty(cut.FindAll("[role=region]"));
        Assert.NotNull(handed[^1]);

        var hosted = Render(handed[^1]!);
        var region = hosted.Find(".rowacts__snippet [role=region]");
        Assert.Contains("h2h snippet", region.TextContent);

        // Still the region the segmented control points at.
        Assert.Equal(region.GetAttribute("id"),
            cut.Find(".rowacts__keys [role=radiogroup]").GetAttribute("aria-controls"));
    }

    [Fact]
    public void Closing_a_hosted_snippet_hands_the_host_nothing()
    {
        var handed = new List<RenderFragment?>();
        var cut = HostedStrip([Snippet("h2h", "Head to head")], handed);

        cut.Find(".rowacts__keys button").Click();
        Assert.NotNull(handed[^1]);

        cut.Find(".rowacts__keys button").Click();
        Assert.Null(handed[^1]);
        Assert.Equal("false", cut.Find(".rowacts__keys button").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// Escape closes a snippet wherever it is drawn: pressed inside the hosted one, it reaches
    /// the strip that owns it, and a click there stays out of the grid row's own click.
    /// </summary>
    [Fact]
    public void Escape_inside_a_hosted_snippet_closes_it()
    {
        var handed = new List<RenderFragment?>();
        var cut = HostedStrip([Snippet("odds", "Odds"), Snippet("h2h", "Head to head")], handed);

        cut.FindAll(".rowacts__keys [role=radio]")[0].Click();
        var hosted = Render(handed[^1]!);

        hosted.Find(".rowacts__snippet").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(handed[^1]);
        Assert.All(cut.FindAll(".rowacts__keys [role=radio]"),
            v => Assert.Equal("false", v.GetAttribute("aria-checked")));
    }
}
