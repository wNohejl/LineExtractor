using Bunit;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// The header's own menu: a trigger named by its label that announces a menu, and a popover
/// holding a <c>DeskMenu</c> of the caller's items. It opens and closes on the trigger, takes
/// its name to the menu, and gives focus back to the trigger when it closes. The brand trigger
/// draws the wordmark instead of an icon, because what it opens is about the whole desk.
/// </summary>
public class RailMenuTests : DeskTestContext
{
    private static readonly RenderFragment Items = b =>
    {
        foreach (var label in new[] { "New window", "New note" })
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "role", "menuitem");
            b.AddContent(2, label);
            b.CloseElement();
        }
    };

    private IRenderedComponent<RailMenu> Menu(string? trigger = null)
        => RenderComponent<RailMenu>(p => p
            .Add(x => x.Label, "Workspaces")
            .Add(x => x.Icon, Icons.Material.Filled.Apps)
            .Add(x => x.Trigger, trigger)
            .Add(x => x.ChildContent, Items));

    [Fact]
    public void The_trigger_is_named_by_its_label_and_announces_a_closed_menu()
    {
        var trigger = Menu().Find("button[aria-haspopup]");

        Assert.Equal("Workspaces", trigger.GetAttribute("aria-label"));
        Assert.Equal("Workspaces", trigger.GetAttribute("title"));
        Assert.Equal("menu", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal("true", trigger.GetAttribute("data-popover-return"));
        Assert.Equal("button", trigger.GetAttribute("type"));
    }

    [Fact]
    public void Closed_it_renders_no_menu()
    {
        Assert.Empty(Menu().FindAll("[role=menu]"));
    }

    [Fact]
    public void Pressing_the_trigger_opens_its_menu_of_the_callers_items()
    {
        var cut = Menu();

        cut.Find("button[aria-haspopup]").Click();

        var menu = cut.Find("[role=menu]");
        Assert.Equal("Workspaces", menu.GetAttribute("aria-label"));
        Assert.Equal(["New window", "New note"], menu.QuerySelectorAll("[role=menuitem]").Select(i => i.TextContent));
        Assert.Equal("true", cut.Find("button[aria-haspopup]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Pressing_it_again_closes_the_menu()
    {
        var cut = Menu();

        cut.Find("button[aria-haspopup]").Click();
        cut.Find("button[aria-haspopup]").Click();

        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    /// <summary>A caller closes the menu after one of its items has run.</summary>
    [Fact]
    public void Close_puts_the_menu_away()
    {
        var cut = Menu();

        cut.Find("button[aria-haspopup]").Click();
        cut.InvokeAsync(cut.Instance.Close);

        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal("false", cut.Find("button[aria-haspopup]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void An_icon_trigger_draws_its_icon()
    {
        var trigger = Menu().Find("button[aria-haspopup]");

        Assert.Contains("rail__btn", trigger.ClassList);
        Assert.NotNull(trigger.QuerySelector("svg"));
    }

    [Fact]
    public void The_brand_trigger_draws_the_wordmark()
    {
        var trigger = Menu(trigger: "brand").Find("button[aria-haspopup]");

        Assert.Contains("hdr__brand--menu", trigger.ClassList);
        Assert.Equal("DESKTEST", trigger.QuerySelector("span")?.TextContent);
        Assert.Equal("TEST", trigger.QuerySelector("em")?.TextContent);
    }
}
