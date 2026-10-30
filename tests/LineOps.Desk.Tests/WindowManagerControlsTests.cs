using Bunit;
using LineOps.Desk.Theming;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// The Window manager's controls, each the one its job calls for: a family of themes is one
/// choice among several, so it is a radiogroup; a count is a value, so it is a field with a
/// stepper beside it rather than a bare number between two keys; a share is a value along a
/// range, so it is a slider.
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

        // The desk's radio group, each radio painting its family's tile inside it and named by
        // the family it offers.
        var radios = group.QuerySelectorAll("[role=radio]");
        Assert.Equal(DeskThemeFamilies.All.Count, radios.Length);
        Assert.All(radios, r =>
        {
            Assert.NotNull(r.QuerySelector(".swatch"));
            Assert.Null(r.GetAttribute("aria-pressed"));
        });
        Assert.Equal(DeskThemeFamilies.All.Select(DeskThemeFamilies.Name), radios.Select(r => r.TextContent.Trim()));

        // One checked, and it is the group's one Tab stop; its tile wears the ring.
        var on = Assert.Single(radios, r => r.GetAttribute("aria-checked") == "true");
        Assert.Equal("0", on.GetAttribute("tabindex"));
        Assert.Contains("swatch--on", on.QuerySelector(".swatch")!.ClassName);
        Assert.All(radios.Where(r => r != on), r =>
        {
            Assert.Equal("false", r.GetAttribute("aria-checked"));
            Assert.Equal("-1", r.GetAttribute("tabindex"));
        });

        // Arrows move the choice, as in every other radiogroup on the desk.
        var theme = Services.GetRequiredService<ThemeService>();
        var first = theme.Family;

        cut.Find(".swatches").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal(DeskThemeFamilies.All[DeskThemeFamilies.All.ToList().IndexOf(first) + 1], theme.Family);
        Assert.Equal("true", cut.FindAll(".swatches [role=radio]")[1].GetAttribute("aria-checked"));
    }

    /// <summary>
    /// The primary window's share is a value along a range, so it is the desk's slider: the reader
    /// hears the percentage, the readout beside it says the same, and dragging moves the share.
    /// </summary>
    [Fact]
    public void The_primary_share_is_a_slider()
    {
        Manager.UpdateSettings(s =>
        {
            s.PrimaryWindowKey = Manager.Catalog.All.First(d => !d.RequiresSubject).Key;
            s.PrimaryShare = 0.45;
        });

        var cut = RenderComponent<WindowManagerPanel>();

        var slider = cut.Find(".slider");
        Assert.Contains("wgroup__row--form", slider.ParentElement!.ClassName);
        Assert.Empty(cut.FindAll(".wm-share"));

        var input = slider.QuerySelector("input[type=range]")!;
        Assert.Equal("The primary window's share of the row", input.GetAttribute("aria-label"));
        Assert.Equal("20", input.GetAttribute("min"));
        Assert.Equal("80", input.GetAttribute("max"));
        Assert.Equal("5", input.GetAttribute("step"));
        Assert.Equal("45%", input.GetAttribute("aria-valuetext"));
        Assert.Equal("45%", slider.QuerySelector(".slider__value")!.TextContent.Trim());

        cut.Find(".slider input[type=range]").Input("60");

        Assert.Equal(0.6, Manager.Settings.PrimaryShare, 3);
        Assert.Equal("60%", cut.Find(".slider__value").TextContent.Trim());
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

    /// <summary>A key inside a sentence reads as a link drawn the wrong shape; each is a row of the group it resets.</summary>
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

        // Each is a row of the group it resets, in the control column.
        var reset = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset to 100%");
        Assert.Contains("desk-btn--plain", reset.GetAttribute("class"));
        Assert.Contains("wgroup__row--form", reset.ParentElement!.ClassName);

        var widths = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset widths");
        Assert.Contains("desk-btn--plain", widths.GetAttribute("class"));
        Assert.Contains("wgroup__row--form", widths.ParentElement!.ClassName);
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
    /// <summary>
    /// A settings screen, drawn as one (HIG Settings, forms.md): every setting sits in a grouped
    /// list under a quiet header with its explanation as the footer, labels leading and controls
    /// trailing. No metric tiles and no note above everything; the readings are value rows.
    /// </summary>
    [Fact]
    public void Settings_are_grouped_lists_with_a_header_and_a_footer()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var sections = cut.FindAll(".wm > .wsection");
        Assert.Equal(
            ["Appearance", "Scale", "Windows", "Primary window", "Resolution", "Workspaces"],
            sections.Select(s => s.QuerySelector("h3.wgroup__head")!.TextContent.Trim()));

        Assert.All(sections, s =>
        {
            Assert.Single(s.QuerySelectorAll(":scope > .wgroup"));
            Assert.NotNull(s.QuerySelector(":scope > p.wgroup__foot"));
        });

        // Every form row leads with its label.
        Assert.All(cut.FindAll(".wgroup__row--form"), row =>
            Assert.Contains("wgroup__label", row.FirstElementChild!.ClassName));

        Assert.Empty(cut.FindAll(".metrics"));
        Assert.Empty(cut.FindAll(".psection"));

        // The tiles' readings are rows of the Windows group.
        var windows = sections[2];
        Assert.Contains("Open now", windows.TextContent);
        Assert.Contains($"{Manager.OpenCount} of {Manager.Settings.MaxConcurrentWindows}", windows.TextContent);
        Assert.Contains("Fits readably", windows.TextContent);
    }

    /// <summary>A limit above what the desk fits is a cost, so it is said in words under its group.</summary>
    [Fact]
    public void A_limit_above_the_desk_is_a_warning_under_the_windows_group()
    {
        Manager.UpdateSettings(s => s.MaxConcurrentWindows = 12);

        var cut = RenderComponent<WindowManagerPanel>();

        if (Manager.RecommendedCapacity >= 12)
            return;

        var windows = cut.FindAll(".wm > .wsection")[2];
        Assert.NotNull(windows.QuerySelector(".note--warn"));
        Assert.NotNull(windows.QuerySelector(".wm-value--warn"));
    }

    /// <summary>A Save that cannot be pressed says what it waits for.</summary>
    [Fact]
    public void Save_says_what_it_waits_for()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var save = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save");
        Assert.True(save.HasAttribute("disabled"));
        Assert.Equal("Name the desk first.", save.GetAttribute("title"));
    }

    /// <summary>
    /// On a desk others can reach, the Window manager says who is at it and offers the way in or
    /// out. The Desk knows no gate of its own: whether there is one, and where its sign-in and
    /// sign-out pages are, it reads from <see cref="LineOps.Desk.IDeskAccess"/>.
    /// </summary>
    [Fact]
    public void A_gated_desk_offers_sign_out()
    {
        Services.AddSingleton<LineOps.Desk.IDeskAccess>(new GatedAccess(operate: true));

        var cut = RenderComponent<WindowManagerPanel>();

        Assert.Contains("Operator", cut.Find(".ptoolbar").TextContent);
        var signOut = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Sign out");

        signOut.Click();

        Assert.EndsWith("/out", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    /// <summary>A visitor on the gated desk is offered the operator's sign-in, at the path the access names.</summary>
    [Fact]
    public void A_visitor_on_a_gated_desk_is_offered_sign_in()
    {
        Services.AddSingleton<LineOps.Desk.IDeskAccess>(new GatedAccess(operate: false));

        var cut = RenderComponent<WindowManagerPanel>();

        Assert.Contains("Visitor", cut.Find(".ptoolbar").TextContent);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Sign out");

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Operator sign-in").Click();

        Assert.EndsWith("/in", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    /// <summary>A desk with one person at it has no gate, so the panel draws no line about who is there.</summary>
    [Fact]
    public void An_open_desk_offers_no_sign_in_or_out()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        Assert.Empty(cut.FindAll(".ptoolbar"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "Sign out" or "Operator sign-in");
    }

    private sealed class GatedAccess(bool operate) : LineOps.Desk.IDeskAccess
    {
        public bool CanOperate => operate;
        public bool IsGated => true;
        public string? SignInPath => "/in";
        public string? SignOutPath => "/out";
    }
}
