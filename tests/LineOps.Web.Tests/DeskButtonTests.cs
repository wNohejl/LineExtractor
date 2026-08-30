using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// What a button promises, checked at the seam. Per ADR 0016 the caller states two
/// things — how loud (Emphasis) and whether it destroys (Role) — and the class list is
/// the contract that carries both into CSS.
/// </summary>
public class DeskButtonTests : DeskTestContext
{
    [Fact]
    public void Renders_its_label()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Ingest"));

        Assert.Contains("Ingest", cut.Find("button").TextContent);
    }

    [Theory]
    [InlineData(DeskEmphasis.Plain, "desk-btn--plain")]
    [InlineData(DeskEmphasis.Tinted, "desk-btn--tinted")]
    [InlineData(DeskEmphasis.Filled, "desk-btn--filled")]
    public void Emphasis_maps_to_its_class(DeskEmphasis emphasis, string expected)
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, emphasis)
            .AddChildContent("Go"));

        var classes = ClassList(cut);

        Assert.Contains("desk-btn", classes);
        Assert.Contains(expected, classes);
    }

    /// <summary>
    /// Plain is the default because most buttons on a dense console are chrome. A
    /// default of Filled would make every panel shout.
    /// </summary>
    [Fact]
    public void Default_emphasis_is_plain()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Go"));

        Assert.Contains("desk-btn--plain", ClassList(cut));
    }

    [Fact]
    public void Destructive_is_marked_independently_of_emphasis()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Role, DeskRole.Destructive)
            .AddChildContent("Delete"));

        var classes = ClassList(cut);

        Assert.Contains("desk-btn--filled", classes);
        Assert.Contains("desk-btn--destructive", classes);
    }

    [Fact]
    public void A_normal_button_says_nothing_about_role()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Save"));

        Assert.DoesNotContain("desk-btn--destructive", ClassList(cut));
    }

    [Theory]
    [InlineData(DeskKeySize.Small, "desk-btn--sm")]
    [InlineData(DeskKeySize.Large, "desk-btn--lg")]
    public void Size_maps_to_its_class(DeskKeySize size, string expected)
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Size, size)
            .AddChildContent("Go"));

        Assert.Contains(expected, ClassList(cut));
    }

    [Fact]
    public void Medium_size_adds_no_class()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Size, DeskKeySize.Medium)
            .AddChildContent("Go"));

        var classes = ClassList(cut);

        Assert.DoesNotContain("desk-btn--sm", classes);
        Assert.DoesNotContain("desk-btn--lg", classes);
    }

    /// <summary>
    /// Busy is not disabled. A button that has started work keeps its emphasis and
    /// refuses further presses — it must not fall back to the grey of a dead control.
    /// </summary>
    [Fact]
    public void Busy_keeps_its_emphasis_and_is_announced_as_busy()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Busy, true)
            .AddChildContent("Ingesting"));

        var button = cut.Find("button");

        Assert.Contains("desk-btn--busy", button.GetAttribute("class"));
        Assert.Contains("desk-btn--filled", button.GetAttribute("class"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
    }

    [Fact]
    public void A_button_that_is_not_busy_says_nothing_about_it()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Ingest"));

        var button = cut.Find("button");

        Assert.Null(button.GetAttribute("aria-busy"));
        Assert.DoesNotContain("desk-btn--busy", button.GetAttribute("class"));
    }

    [Fact]
    public void Busy_refuses_further_presses()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Busy, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingesting"));

        cut.Find("button").Click();

        Assert.Equal(0, presses);
    }

    [Fact]
    public void An_idle_button_reports_its_press()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingest"));

        cut.Find("button").Click();

        Assert.Equal(1, presses);
    }

    [Fact]
    public void Disabled_stops_the_press()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Disabled, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingest"));

        cut.Find("button").Click();

        Assert.Equal(0, presses);
    }

    [Fact]
    public void An_icon_without_a_label_becomes_a_square()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .Add(x => x.Title, "Refresh"));

        var button = cut.Find("button");

        Assert.Contains("desk-btn--icon", button.GetAttribute("class"));
        Assert.Equal("Refresh", button.GetAttribute("title"));
    }

    [Fact]
    public void An_icon_beside_a_label_is_not_a_square()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .AddChildContent("Refresh"));

        Assert.DoesNotContain("desk-btn--icon", ClassList(cut));
    }

    [Fact]
    public void A_one_off_class_is_appended_last_so_it_can_win()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Class, "panel__commit")
            .AddChildContent("Save"));

        var classes = ClassList(cut);

        Assert.EndsWith("panel__commit", classes.Trim());
        Assert.Contains("desk-btn--filled", classes);
    }

    [Fact]
    public void Unmatched_attributes_reach_the_button()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .AddUnmatched("data-testid", "commit")
            .AddChildContent("Save"));

        Assert.Equal("commit", cut.Find("button").GetAttribute("data-testid"));
    }

    [Fact]
    public void Button_type_defaults_to_button_so_a_press_never_submits_by_accident()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Save"));

        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
    }

    [Fact]
    public void Button_type_can_be_asked_to_submit()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.ButtonType, MudBlazor.ButtonType.Submit)
            .AddChildContent("Save"));

        Assert.Equal("submit", cut.Find("button").GetAttribute("type"));
    }

    private static string ClassList(IRenderedComponent<DeskButton> cut)
        => cut.Find("button").GetAttribute("class") ?? string.Empty;
}
