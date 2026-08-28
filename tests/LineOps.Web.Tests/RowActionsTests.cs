using Bunit;
using LineOps.Web.Components.Desk;
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

    private IRenderedComponent<RowActions> Strip(IReadOnlyList<RowAction> actions, string? label = null)
        => RenderComponent<RowActions>(p => p
            .Add(x => x.Actions, actions)
            .Add(x => x.Label, label));

    [Fact]
    public void Renders_a_key_per_action()
    {
        var cut = Strip([Snippet("h2h", "Head to head"), Snippet("last5", "Last five")]);

        Assert.Equal(["Head to head", "Last five"],
            cut.FindAll(".rowacts__keys button").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void The_caption_is_optional()
    {
        Assert.Empty(Strip([Snippet("h2h", "Head to head")]).FindAll(".board__actions-label"));

        Assert.Equal("ACTIONS",
            Strip([Snippet("h2h", "Head to head")], "ACTIONS").Find(".board__actions-label").TextContent);
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
        Assert.Contains("desk-key--on", key.GetAttribute("class"));
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

    /// <summary>Pressing the open action again returns the row to its keys.</summary>
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

        cut.FindAll(".rowacts__keys button")[0].Click();
        cut.FindAll(".rowacts__keys button")[1].Click();

        var regions = cut.FindAll("[role=region]");

        Assert.Single(regions);
        Assert.Contains("last5 snippet", regions[0].TextContent);

        var keys = cut.FindAll(".rowacts__keys button");
        Assert.Equal("false", keys[0].GetAttribute("aria-pressed"));
        Assert.Equal("true", keys[1].GetAttribute("aria-pressed"));
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
}
