using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// What a key promises, checked at the seam. See ADR 0008: the caller says what the key
/// does (Tone) and the class list is the contract that carries it into CSS.
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
    [InlineData(DeskTone.Neutral, "desk-key--neutral")]
    [InlineData(DeskTone.Action, "desk-key--action")]
    [InlineData(DeskTone.Go, "desk-key--go")]
    [InlineData(DeskTone.Stop, "desk-key--stop")]
    [InlineData(DeskTone.Caution, "desk-key--caution")]
    public void Tone_maps_to_its_class(DeskTone tone, string expected)
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Tone, tone)
            .AddChildContent("Go"));

        var classes = ClassList(cut);

        Assert.Contains("desk-key", classes);
        Assert.Contains(expected, classes);
    }

    [Fact]
    public void Default_tone_is_neutral()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Go"));

        Assert.Contains("desk-key--neutral", ClassList(cut));
    }

    [Theory]
    [InlineData(DeskKeySize.Small, "desk-key--sm")]
    [InlineData(DeskKeySize.Large, "desk-key--lg")]
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

        Assert.DoesNotContain("desk-key--sm", classes);
        Assert.DoesNotContain("desk-key--lg", classes);
    }

    [Fact]
    public void Quiet_marks_the_key_as_an_escape()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Quiet, true)
            .AddChildContent("Cancel"));

        Assert.Contains("desk-key--quiet", ClassList(cut));
    }

    [Fact]
    public void Quiet_is_off_by_default()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Cancel"));

        Assert.DoesNotContain("desk-key--quiet", ClassList(cut));
    }

    /// <summary>
    /// ADR 0008, "busy is not disabled": an occupied key keeps its cap and its tone. It
    /// refuses further presses, but it must not fall back to the grey of a dead control —
    /// so the tone class stays and a busy class is added alongside it.
    /// </summary>
    [Fact]
    public void Busy_keeps_its_tone_and_is_announced_as_busy()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Tone, DeskTone.Go)
            .Add(x => x.Busy, true)
            .AddChildContent("Ingesting"));

        var button = cut.Find("button");

        Assert.Contains("desk-key--busy", button.GetAttribute("class"));
        Assert.Contains("desk-key--go", button.GetAttribute("class"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
    }

    [Fact]
    public void A_key_that_is_not_busy_says_nothing_about_it()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Ingest"));

        var button = cut.Find("button");

        Assert.Null(button.GetAttribute("aria-busy"));
        Assert.DoesNotContain("desk-key--busy", button.GetAttribute("class"));
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
    public void An_idle_key_reports_its_press()
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

    /// <summary>An icon with no label is a square cap, not a clipped button.</summary>
    [Fact]
    public void An_icon_without_a_label_becomes_a_cap()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .Add(x => x.Title, "Refresh"));

        var button = cut.Find("button");

        Assert.Contains("desk-key--icon", button.GetAttribute("class"));
        Assert.Equal("Refresh", button.GetAttribute("title"));
    }

    [Fact]
    public void An_icon_beside_a_label_is_not_a_cap()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .AddChildContent("Refresh"));

        Assert.DoesNotContain("desk-key--icon", ClassList(cut));
    }

    [Fact]
    public void A_one_off_class_is_appended_last_so_it_can_win()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Tone, DeskTone.Action)
            .Add(x => x.Class, "panel__commit")
            .AddChildContent("Save"));

        var classes = ClassList(cut);

        Assert.EndsWith("panel__commit", classes.Trim());
        Assert.Contains("desk-key--action", classes);
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
    public void Button_type_defaults_to_button_so_a_key_never_submits_by_accident()
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
