using Bunit;
using LineOps.Desk.Theming;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Window manager's controls, each the one its job calls for: a family of themes is one
/// choice among several, so it is a radiogroup; a count is a value, so it is a field with a
/// stepper beside it rather than a bare number between two keys.
/// </summary>
public class WindowManagerControlsTests : DeskTestContext
{
    private WindowManager Manager => Services.GetRequiredService<WindowManager>();

    [Fact]
    public void Swatches_are_a_radiogroup()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var group = cut.Find(".swatches");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal("Theme", group.GetAttribute("aria-label"));

        var swatches = cut.FindAll(".swatch");
        Assert.Equal(DeskThemeFamilies.All.Count, swatches.Count);
        Assert.All(swatches, s =>
        {
            Assert.Equal("radio", s.GetAttribute("role"));
            Assert.Null(s.GetAttribute("aria-pressed"));
        });

        // One checked, and it is the group's one Tab stop.
        var on = Assert.Single(swatches, s => s.GetAttribute("aria-checked") == "true");
        Assert.Equal("0", on.GetAttribute("tabindex"));
        Assert.All(swatches.Where(s => s != on), s =>
        {
            Assert.Equal("false", s.GetAttribute("aria-checked"));
            Assert.Equal("-1", s.GetAttribute("tabindex"));
        });

        // Arrows move the choice, as in every other radiogroup on the desk.
        var theme = Services.GetRequiredService<ThemeService>();
        var first = theme.Family;

        cut.Find(".swatches").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal(DeskThemeFamilies.All[DeskThemeFamilies.All.ToList().IndexOf(first) + 1], theme.Family);
        Assert.Equal("true", cut.FindAll(".swatch")[1].GetAttribute("aria-checked"));
    }

    [Fact]
    public void Concurrent_windows_is_a_field_with_a_stepper()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var field = cut.Find("input[aria-label='Concurrent windows']");
        Assert.Equal("number", field.GetAttribute("type"));
        Assert.Equal("1", field.GetAttribute("min"));
        Assert.Equal("12", field.GetAttribute("max"));
        Assert.Equal(Manager.Settings.MaxConcurrentWindows.ToString(), field.GetAttribute("value"));

        // The count no longer sits as a bare figure between the keys.
        Assert.Empty(cut.FindAll(".readout"));

        // Typed, and clamped to the same bounds the stepper keeps.
        cut.Find("input[aria-label='Concurrent windows']").Change("6");
        Assert.Equal(6, Manager.Settings.MaxConcurrentWindows);

        cut.Find("input[aria-label='Concurrent windows']").Change("40");
        Assert.Equal(12, Manager.Settings.MaxConcurrentWindows);

        // The stepper beside it nudges by one.
        cut.Find("button[aria-label='Concurrent windows: one fewer']").Click();
        Assert.Equal(11, Manager.Settings.MaxConcurrentWindows);
        Assert.Equal("11", cut.Find("input[aria-label='Concurrent windows']").GetAttribute("value"));

        // Matching the desk is a Plain key after the field.
        var match = cut.FindAll("button").Single(b => b.TextContent.Contains("Match this desk"));
        Assert.Contains("desk-btn--plain", match.GetAttribute("class"));
        Assert.Contains($"({Manager.RecommendedCapacity})", match.TextContent);

        match.Click();
        Assert.Equal(Manager.RecommendedCapacity, Manager.Settings.MaxConcurrentWindows);
    }

    /// <summary>
    /// 80% put the desk's captions under 10px, below the type floor; the interface scale
    /// starts at 90%.
    /// </summary>
    [Fact]
    public void Interface_scale_starts_at_ninety_percent()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var options = cut.Find("[role='radiogroup'][aria-label='Interface scale']").QuerySelectorAll(".gate__opt");
        Assert.Equal("90%", options[0].TextContent.Trim());
        Assert.DoesNotContain(options, o => o.TextContent.Trim() == "80%");
    }

    /// <summary>A key inside a sentence reads as a link drawn the wrong shape; each sits on a row of its own.</summary>
    [Fact]
    public void Reset_keys_sit_on_rows_not_inside_sentences()
    {
        Manager.UpdateSettings(s =>
        {
            s.UiScale = 1.25;
            s.PrimaryWindowKey = Manager.Catalog.All.First(d => !d.RequiresSubject).Key;
        });

        var cut = RenderComponent<WindowManagerPanel>();

        Assert.Empty(cut.FindAll("p button"));

        var reset = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset to 100%");
        Assert.Contains("desk-btn--plain", reset.GetAttribute("class"));
        Assert.Equal("row", reset.ParentElement!.ClassName);

        var widths = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset widths");
        Assert.Contains("desk-btn--plain", widths.GetAttribute("class"));
        Assert.Equal("row", widths.ParentElement!.ClassName);
        Assert.Contains("until you reset the widths.", cut.Markup);
    }

    /// <summary>
    /// The nudge is one stepper beside the field — a capsule of two halves — not two loose
    /// keys with a gap between them, and a half that would cross a bound is off.
    /// </summary>
    [Fact]
    public void The_window_count_stepper_is_one_capsule_beside_the_field()
    {
        Manager.UpdateSettings(s => s.MaxConcurrentWindows = 12);

        var cut = RenderComponent<WindowManagerPanel>();

        var stepper = cut.Find(".stepper");
        Assert.Equal("Concurrent windows", stepper.GetAttribute("aria-label"));
        Assert.Equal("INPUT", stepper.PreviousElementSibling!.TagName);
        Assert.Empty(cut.FindAll("button[aria-label='One fewer window'], button[aria-label='One more window']"));

        Assert.True(cut.Find("button[aria-label='Concurrent windows: one more']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Concurrent windows: one fewer']").Click();
        Assert.Equal(11, Manager.Settings.MaxConcurrentWindows);
        Assert.False(cut.Find("button[aria-label='Concurrent windows: one more']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Concurrent windows: one more']").Click();
        Assert.Equal(12, Manager.Settings.MaxConcurrentWindows);
    }
}
