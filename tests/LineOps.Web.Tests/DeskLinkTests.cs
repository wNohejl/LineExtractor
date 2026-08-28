using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Tests;

/// <summary>
/// A cell that names where a click goes. The shortcut is reserved rather than revealed, so
/// it is always in the markup and always hidden from the reading order.
/// </summary>
public class DeskLinkTests : DeskTestContext
{
    [Fact]
    public void Renders_its_label_inside_the_label_span()
    {
        var cut = RenderComponent<DeskLink>(p => p
            .Add(x => x.Destination, "movement")
            .AddChildContent("Lakers @ Suns"));

        Assert.Equal("Lakers @ Suns", cut.Find(".desklink__label").TextContent);
    }

    [Fact]
    public void Is_a_plain_button_so_it_never_submits_a_form()
    {
        var cut = RenderComponent<DeskLink>(p => p.Add(x => x.Destination, "movement"));

        var button = cut.Find("button.desklink");

        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Fact]
    public void Title_names_the_destination_by_default()
    {
        var cut = RenderComponent<DeskLink>(p => p.Add(x => x.Destination, "movement"));

        Assert.Equal("Open movement", cut.Find("button").GetAttribute("title"));
    }

    [Fact]
    public void Tooltip_overrides_the_default_title()
    {
        var cut = RenderComponent<DeskLink>(p => p
            .Add(x => x.Destination, "movement")
            .Add(x => x.Tooltip, "See every tick since open"));

        Assert.Equal("See every tick since open", cut.Find("button").GetAttribute("title"));
    }

    /// <summary>
    /// The shortcut repeats the title in visual form. Reading it aloud as well would say the
    /// destination twice, so it is hidden from assistive tech.
    /// </summary>
    [Fact]
    public void The_shortcut_is_reserved_and_hidden_from_the_reading_order()
    {
        var cut = RenderComponent<DeskLink>(p => p.Add(x => x.Destination, "movement"));

        var shortcut = cut.Find(".desklink__shortcut");

        Assert.Equal("true", shortcut.GetAttribute("aria-hidden"));
        Assert.Contains("movement", shortcut.TextContent);
    }

    [Fact]
    public void Reports_its_click()
    {
        var clicks = 0;

        var cut = RenderComponent<DeskLink>(p => p
            .Add(x => x.Destination, "movement")
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++))
            .AddChildContent("Lakers @ Suns"));

        cut.Find("button").Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void A_one_off_class_joins_the_base_class()
    {
        var cut = RenderComponent<DeskLink>(p => p
            .Add(x => x.Destination, "movement")
            .Add(x => x.Class, "cell--wide"));

        var classes = cut.Find("button").GetAttribute("class") ?? string.Empty;

        Assert.Contains("desklink", classes);
        Assert.Contains("cell--wide", classes);
    }
}
