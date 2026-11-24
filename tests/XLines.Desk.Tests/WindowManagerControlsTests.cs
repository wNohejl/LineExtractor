using Bunit;
using XLines.Desk.Theming;
using XLines.Desk.Windowing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Desk.Tests;

/// <summary>
/// The Window manager's controls, each the one its job calls for: a family of themes is one
/// choice among several, so it is a radiogroup; a count is a value, so it is a field with a
/// stepper beside it rather than a bare number between two keys; a share is a value along a
/// range, so it is a slider.
/// </summary>
public class WindowManagerControlsTests : DeskTestContext
{
    private WindowManager Manager => Services.GetRequiredService<WindowManager>();

    /// <summary>
    /// The themes are their own card: one grid, one radiogroup, its tiles filtered by a segmented
    /// control in the card's header bar — All, then the catalogue's headings — which opens on
    /// All, so a card spanning a wide row is never a large box holding two tiles. No shelves, no headings.
    /// </summary>
    [Fact]
    public void Swatches_are_one_radiogroup_filtered_from_the_themes_card_s_bar()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var card = cut.FindAll(".wm > .wsection").Single(s => s.QuerySelector(".themes") is not null);
        Assert.Equal("Themes", card.QuerySelector(".wgroup__bar > .wgroup__title")!.TextContent.Trim());
        Assert.Contains("wsection--rest", card.ClassList);

        var filter = card.QuerySelector(".wgroup__bar > .wgroup__keys [role=radiogroup]")!;
        Assert.Equal("Show themes", filter.GetAttribute("aria-label"));
        Assert.Equal(["All", .. DeskThemeFamilies.Groups], filter.QuerySelectorAll(".gate__opt").Select(o => o.TextContent.Trim()));
        // It opens on All: one heading's tiles in a card that spans the row were an empty box.
        Assert.Equal("All", filter.QuerySelector("[aria-checked=true]")!.TextContent.Trim());

        var group = cut.Find(".themes");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal("Theme", group.GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".themes__group, .themes__head"));

        // A heading narrows the grid to its families, and All brings every one back.
        cut.FindAll("[aria-label='Show themes'] .gate__opt").Single(o => o.TextContent.Trim() == DeskThemeGroups.Vivid).Click();
        Assert.Equal(DeskThemeFamilies.Catalog.Where(f => f.Group == DeskThemeGroups.Vivid).Select(f => f.Name),
            cut.FindAll(".themes [role=radio]").Select(r => r.TextContent.Trim()));
        Assert.Contains($"Showing {DeskThemeFamilies.Name(DeskThemeFamily.Standard)}", cut.Find(".wm > .wsection--rest > p.wgroup__foot").TextContent);

        // All: every family a tile, in the catalogue's reading order, named by the family it offers
        // and painted by its own block.
        cut.FindAll("[aria-label='Show themes'] .gate__opt").Single(o => o.TextContent.Trim() == "All").Click();
        var radios = cut.FindAll(".themes [role=radio]");
        Assert.Equal(DeskThemeFamilies.All.Count, radios.Count);
        Assert.Equal(DeskThemeFamilies.All.Select(DeskThemeFamilies.Name), radios.Select(r => r.TextContent.Trim()));
        Assert.Equal(DeskThemeFamilies.All.Select(f => DeskThemeFamilies.Attribute(f, true)),
            radios.Select(r => r.QuerySelector(".swatch")!.GetAttribute("data-theme")));

        // One checked, and it is the group's one Tab stop; its tile says so by a ring, a tick and
        // its name's weight, not by hue alone.
        var on = Assert.Single(radios, r => r.GetAttribute("aria-checked") == "true");
        Assert.Equal("0", on.GetAttribute("tabindex"));
        Assert.Contains("swatch--on", on.QuerySelector(".swatch")!.ClassName);
        Assert.NotNull(on.QuerySelector(".swatch__tick"));
        Assert.All(radios.Where(r => r != on), r =>
        {
            Assert.Equal("false", r.GetAttribute("aria-checked"));
            Assert.Equal("-1", r.GetAttribute("tabindex"));
            Assert.Null(r.QuerySelector(".swatch__tick"));
        });

        // The footer names the theme showing, which a filter may hide.
        Assert.Contains($"Showing {DeskThemeFamilies.Name(DeskThemeFamily.Standard)}", card.QuerySelector(":scope > p.wgroup__foot")!.TextContent);
    }

    /// <summary>
    /// Under All the arrows travel every tile in reading order, across what were the headings, and
    /// stop at the ends; Home and End reach the first and the last.
    /// </summary>
    [Fact]
    public void Arrows_travel_the_tiles_across_the_headings()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();
        var all = DeskThemeFamilies.All;

        cut.FindAll("[aria-label='Show themes'] .gate__opt").Single(o => o.TextContent.Trim() == "All").Click();

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(all[1], theme.Family);
        Assert.Equal("true", cut.FindAll(".themes [role=radio]")[1].GetAttribute("aria-checked"));

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal(all[^1], theme.Family);

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(all[^1], theme.Family);

        // Across a heading: the first tile of the second group is one step from the last of the first.
        var firstOfSecond = DeskThemeFamilies.Catalog.First(f => f.Group == DeskThemeFamilies.Groups[1]).Family;
        var index = all.ToList().IndexOf(firstOfSecond);
        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal(all[0], theme.Family);
        for (var i = 0; i < index; i++)
            cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(firstOfSecond, theme.Family);

        cut.Find(".themes").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal(all[index - 1], theme.Family);
    }

    /// <summary>A family's tile wears the accent that family was last given, so it previews what choosing it brings back.</summary>
    [Fact]
    public async Task A_tile_wears_the_accent_its_family_was_last_given()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        await theme.SetAccentAsync(DeskThemeFamily.Standard, "purple");

        var cut = RenderComponent<WindowManagerPanel>();

        var standard = cut.Find($".themes .swatch[data-theme='{DeskThemeFamilies.Attribute(DeskThemeFamily.Standard, true)}']");
        Assert.Equal("purple", standard.GetAttribute("data-accent"));
        Assert.All(cut.FindAll(".themes .swatch").Where(s => s.GetAttribute("data-theme") != standard.GetAttribute("data-theme")), s => Assert.Null(s.GetAttribute("data-accent")));
    }

    /// <summary>
    /// The accent sits beside the mode, the two cells of the split row that opens the Appearance
    /// card, and offers the showing family's own list; a family that offers only its own has
    /// nothing to choose, so the cell is not drawn and the mode takes the row. Choosing a swatch
    /// moves the desk's accent, not just the picker.
    /// </summary>
    [Fact]
    public async Task The_accent_picker_offers_the_showing_family_s_list()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();

        Assert.Equal(DeskThemeFamily.Standard, theme.ShowingFamily);
        var picker = cut.Find(".accents");
        Assert.Equal("Accent", picker.GetAttribute("aria-label"));
        Assert.Equal(DeskThemeFamilies.Accents(DeskThemeFamily.Standard).Select(a => a.Key),
            picker.QuerySelectorAll("[role=radio]").Select(r => r.GetAttribute("data-accent")));

        // Beside the mode, in the split row that opens the Appearance card, under its bar.
        var appearance = cut.FindAll(".wm > .wsection")[0];
        var split = appearance.QuerySelector(".wgroup > .wgroup__bar + .wgroup__split")!;
        var cells = split.QuerySelectorAll(":scope > .wgroup__cell");
        Assert.Equal(["Mode", "Accent"], cells.Select(c => c.QuerySelector(".wgroup__label")!.TextContent.Trim()));
        Assert.NotNull(cells[0].QuerySelector("[role=radiogroup][aria-label=Appearance]"));
        Assert.NotNull(cells[1].QuerySelector(".accents"));

        cut.Find(".accents [data-accent=teal]").Click();
        Assert.Equal("teal", theme.Accent.Key);
        Assert.Equal("true", cut.Find(".accents [data-accent=teal]").GetAttribute("aria-checked"));

        foreach (var family in DeskThemeFamilies.All)
        {
            await cut.InvokeAsync(() => theme.SetFamilyAsync(family));

            if (DeskThemeFamilies.Accents(family).Count > 1)
                Assert.Equal(DeskThemeFamilies.Attribute(family, theme.IsDark), cut.Find(".accents [role=radio]").GetAttribute("data-theme"));
            else
            {
                Assert.Empty(cut.FindAll(".accents"));
                Assert.Single(cut.FindAll(".wgroup__split > .wgroup__cell"));
            }
        }
    }

    /// <summary>
    /// The four makes are gates in the Appearance group, default first, each moving the desk when
    /// chosen; and one key puts the whole group back, offered only while there is something to.
    /// </summary>
    [Fact]
    public void The_makes_are_gates_and_one_key_resets_the_appearance()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();
        var appearance = cut.FindAll(".wm > .wsection")[0];

        (string Label, string[] Options)[] gates =
        [
            ("Corners", ["Standard", "Sharp", "Round"]),
            ("Density", ["Standard", "Compact", "Roomy"]),
            ("Backdrop", ["Dots", "Plain", "Gradient", "Grid"]),
            ("Typeface", ["System", "Humanist", "Serif", "Mono"])
        ];

        foreach (var (label, options) in gates)
        {
            var gate = appearance.QuerySelector($"[role='radiogroup'][aria-label='{label}']");
            Assert.NotNull(gate);
            Assert.Equal(options, gate!.QuerySelectorAll(".gate__opt").Select(o => o.TextContent.Trim()));
        }

        Assert.True(theme.IsDefaultAppearance);
        AssertResetDimmed();

        cut.FindAll("[aria-label='Corners'] .gate__opt").Single(o => o.TextContent.Trim() == "Round").Click();
        cut.FindAll("[aria-label='Density'] .gate__opt").Single(o => o.TextContent.Trim() == "Compact").Click();
        cut.FindAll("[aria-label='Backdrop'] .gate__opt").Single(o => o.TextContent.Trim() == "Grid").Click();
        cut.FindAll("[aria-label='Typeface'] .gate__opt").Single(o => o.TextContent.Trim() == "Serif").Click();

        Assert.Equal((DeskShape.Round, DeskDensity.Compact, DeskBackdrop.Grid, DeskTypeface.Serif),
            (theme.Shape, theme.Density, theme.Backdrop, theme.Typeface));

        var reset = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset appearance");
        Assert.Contains("wgroup__row--form", reset.ParentElement!.ParentElement!.ClassName);
        Assert.Contains("wgroup", reset.ParentElement!.ParentElement!.ParentElement!.ClassName);
        Assert.Null(reset.GetAttribute("aria-disabled"));
        Assert.Null(reset.GetAttribute("aria-describedby"));
        Assert.NotNull(cut.FindAll(".wm > .wsection")[0].QuerySelector("[aria-label='Corners']"));

        reset.Click();

        Assert.True(theme.IsDefaultAppearance);
        AssertResetDimmed();

        // Pressed again, dimmed, it refuses: nothing moves and nothing is announced.
        var announced = 0;
        theme.Changed += () => { announced++; return Task.CompletedTask; };
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset appearance").Click();
        Assert.Equal(0, announced);

        // Still drawn, so the focus the press left on it stays on it rather than falling to the
        // page; dimmed rather than disabled, so it keeps its Tab stop; and it says why on its row.
        void AssertResetDimmed()
        {
            var key = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset appearance");
            Assert.Equal("true", key.GetAttribute("aria-disabled"));
            Assert.False(key.HasAttribute("disabled"));
            var why = cut.Find($"#{key.GetAttribute("aria-describedby")}");
            Assert.Equal(key.ParentElement, why.ParentElement);
            Assert.Equal("Everything here is at its default.", why.TextContent.Trim());
        }
    }

    /// <summary>
    /// Every look in DeskLooks.All is a row of the Appearance group, in the descriptor's order and
    /// with its labels — after the accent, before the reset key — so a look added to the table is a
    /// gate on the panel without the panel being touched. Each gate moves the look it names.
    /// </summary>
    [Fact]
    public void Every_look_is_a_gate_in_the_appearance_group_in_its_order()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();
        var appearance = cut.FindAll(".wm > .wsection")[0];

        var labels = appearance.QuerySelectorAll(".wgroup__row > .wgroup__label")
            .Select(l => l.TextContent.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var looks = DeskLooks.All.Select(l => l.Label).ToList();
        var first = labels.IndexOf(looks[0]);

        Assert.Equal(0, first); // The looks are the first form rows, under the mode and accent.
        Assert.NotNull(appearance.QuerySelector(".wgroup__split ~ .wgroup__row--form [aria-label='Glass'], .wgroup__split ~ .wgroup__row--form [aria-label='Corners']"));
        Assert.Equal(looks, labels.Skip(first).Take(looks.Count));

        foreach (var look in DeskLooks.All)
        {
            var gate = appearance.QuerySelector($"[role='radiogroup'][aria-label='{look.Label}']");
            Assert.NotNull(gate);
            Assert.Equal(look.Options.Select(o => o.Label), gate!.QuerySelectorAll(".gate__opt").Select(o => o.TextContent.Trim()));

            var last = look.Options[^1];
            cut.FindAll($"[aria-label='{look.Label}'] .gate__opt").Single(o => o.TextContent.Trim() == last.Label).Click();
            Assert.Equal(last, theme.Chosen(look));
        }
    }

    /// <summary>
    /// The same rows, written out: the test above reads its expectation from DeskLooks.All, which
    /// would agree with a table that lost a row or reordered its options. This one pins what the
    /// operator sees, by hand.
    /// </summary>
    [Fact]
    public void The_appearance_gates_read_as_written()
    {
        var cut = RenderComponent<WindowManagerPanel>();
        var appearance = cut.FindAll(".wm > .wsection")[0];

        (string Label, string[] Options)[] expected =
        [
            ("Glass", ["Clear", "Tinted"]),
            ("Corners", ["Standard", "Sharp", "Round"]),
            ("Density", ["Standard", "Compact", "Roomy"]),
            ("Backdrop", ["Dots", "Plain", "Gradient", "Grid"]),
            ("Typeface", ["System", "Humanist", "Serif", "Mono"])
        ];

        var labels = appearance.QuerySelectorAll(".wgroup__row > .wgroup__label")
            .Select(l => l.TextContent.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var first = labels.IndexOf("Glass");

        Assert.Equal(0, first); // The looks are the first form rows, under the mode and accent.
        Assert.NotNull(appearance.QuerySelector(".wgroup__split ~ .wgroup__row--form [aria-label='Glass'], .wgroup__split ~ .wgroup__row--form [aria-label='Corners']"));
        Assert.Equal(expected.Select(e => e.Label), labels.Skip(first).Take(expected.Length));

        foreach (var (label, options) in expected)
        {
            var gate = appearance.QuerySelector($"[role='radiogroup'][aria-label='{label}']");
            Assert.NotNull(gate);
            Assert.Equal(options, gate!.QuerySelectorAll(".gate__opt").Select(o => o.TextContent.Trim()));
        }
    }

    /// <summary>
    /// Under Increase Contrast a desk never told which family to wear wears High contrast, so that
    /// is the tile ticked — not Standard, the stored default. Pressing Standard then pins it against
    /// the machine, as SetFamilyAsync documents, rather than being taken for a repeat.
    /// </summary>
    [Fact]
    public async Task The_tick_follows_the_family_showing_and_Standard_can_be_pinned()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();

        await cut.InvokeAsync(() => theme.OnSystemPreferenceChanged(theme.IsDark, moreContrast: true, reducedTransparency: false));
        Assert.Equal(DeskThemeFamily.HighContrast, theme.ShowingFamily);

        string Ticked() => cut.FindAll(".themes [role=radio]").Single(r => r.GetAttribute("aria-checked") == "true").TextContent.Trim();
        Assert.Equal(DeskThemeFamilies.Name(DeskThemeFamily.HighContrast), Ticked());

        cut.FindAll(".themes [role=radio]").Single(r => r.TextContent.Trim() == DeskThemeFamilies.Name(DeskThemeFamily.Standard)).Click();

        Assert.Equal(DeskThemeFamily.Standard, theme.ShowingFamily);
        Assert.Equal(DeskThemeFamilies.Name(DeskThemeFamily.Standard), Ticked());
        Assert.False(theme.IsDefaultAppearance);

        // Pinned: the machine asking again does not take it back.
        await cut.InvokeAsync(() => theme.OnSystemPreferenceChanged(theme.IsDark, moreContrast: true, reducedTransparency: false));
        Assert.Equal(DeskThemeFamily.Standard, theme.ShowingFamily);
    }

    /// <summary>
    /// The glass is two positions, so it is the same gate as the mode, in the appearance group under
    /// the theme. Choosing Tinted moves the desk's glass, not just the gate; and while the machine
    /// reduces transparency there is no glass to tint, so the row is not offered and the footer
    /// says why.
    /// </summary>
    [Fact]
    public async Task The_glass_is_a_gate_offered_only_while_there_is_glass_to_tint()
    {
        var theme = Services.GetRequiredService<ThemeService>();
        var cut = RenderComponent<WindowManagerPanel>();

        var gate = cut.Find("[role='radiogroup'][aria-label='Glass']");
        Assert.Equal(["Clear", "Tinted"], gate.QuerySelectorAll(".gate__opt").Select(o => o.TextContent.Trim()));
        Assert.Equal(DeskGlass.Clear, theme.Glass);

        var appearance = cut.FindAll(".wm > .wsection")[0];
        Assert.NotNull(appearance.QuerySelector("[aria-label='Glass']"));
        Assert.Contains("Tinted makes the glass more opaque", appearance.QuerySelector("p.wgroup__foot")!.TextContent);

        cut.FindAll("[aria-label='Glass'] .gate__opt").Single(o => o.TextContent.Trim() == "Tinted").Click();
        Assert.Equal(DeskGlass.Tinted, theme.Glass);

        await cut.InvokeAsync(() => theme.OnSystemPreferenceChanged(theme.IsDark, moreContrast: false, reducedTransparency: true));

        Assert.Empty(cut.FindAll("[aria-label='Glass']"));
        Assert.Contains("reduces transparency", cut.FindAll(".wm > .wsection")[0].QuerySelector("p.wgroup__foot")!.TextContent);
        Assert.Equal(DeskGlass.Tinted, theme.Glass);
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

        var field = cut.Find("input[aria-label='Columns side by side']");
        Assert.Equal("number", field.GetAttribute("type"));
        Assert.Equal("1", field.GetAttribute("min"));
        Assert.Equal("12", field.GetAttribute("max"));
        Assert.Equal(Manager.Settings.MaxConcurrentWindows.ToString(), field.GetAttribute("value"));

        // The count no longer sits as a bare figure between the keys.
        Assert.Empty(cut.FindAll(".readout"));

        // Typed, and clamped to the same bounds the stepper keeps.
        cut.Find("input[aria-label='Columns side by side']").Change("6");
        Assert.Equal(6, Manager.Settings.MaxConcurrentWindows);

        cut.Find("input[aria-label='Columns side by side']").Change("40");
        Assert.Equal(12, Manager.Settings.MaxConcurrentWindows);

        // The stepper beside it nudges by one.
        cut.Find("button[aria-label='Columns side by side: one fewer']").Click();
        Assert.Equal(11, Manager.Settings.MaxConcurrentWindows);
        Assert.Equal("11", cut.Find("input[aria-label='Columns side by side']").GetAttribute("value"));

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
        Assert.Equal("Columns side by side", stepper.GetAttribute("aria-label"));
        Assert.Equal("INPUT", stepper.PreviousElementSibling!.TagName);
        Assert.Empty(cut.FindAll("button[aria-label='One fewer window'], button[aria-label='One more window']"));

        Assert.True(cut.Find("button[aria-label='Columns side by side: one more']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Columns side by side: one fewer']").Click();
        Assert.Equal(11, Manager.Settings.MaxConcurrentWindows);
        Assert.False(cut.Find("button[aria-label='Columns side by side: one more']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Columns side by side: one more']").Click();
        Assert.Equal(12, Manager.Settings.MaxConcurrentWindows);
    }
    /// <summary>
    /// A settings screen, drawn as one (HIG Settings, forms.md): every setting sits in a grouped
    /// list drawn as a section card, named in the bar inside it, with its explanation as the footer
    /// under it, in the card flow (.wflow) rather than one narrow column; labels leading and controls
    /// trailing. No metric tiles and no note above everything; the readings are value rows.
    /// </summary>
    [Fact]
    public void Settings_are_grouped_lists_with_a_header_and_a_footer()
    {
        var cut = RenderComponent<WindowManagerPanel>();

        var sections = cut.FindAll(".wm > .wsection");
        Assert.Equal(
            ["Appearance", "Themes", "Scale", "Windows", "Primary window", "Resolution", "Workspaces"],
            sections.Select(s => s.QuerySelector(".wgroup > .wgroup__bar > h3.wgroup__title")!.TextContent.Trim()));

        Assert.All(sections, s =>
        {
            Assert.Single(s.QuerySelectorAll(":scope > .wgroup"));
            Assert.NotNull(s.QuerySelector(":scope > p.wgroup__foot"));
        });

        Assert.Contains("wflow", cut.Find(".wm").ClassList);
        Assert.Empty(cut.FindAll(".wm .wgroup__head"));

        // Every form row leads with its label.
        Assert.All(cut.FindAll(".wgroup__row--form"), row =>
            Assert.Contains("wgroup__label", row.FirstElementChild!.ClassName));

        Assert.Empty(cut.FindAll(".metrics"));
        Assert.Empty(cut.FindAll(".psection"));

        // The tiles' readings are rows of the Windows group.
        var windows = sections[3];
        Assert.Contains("In use", windows.TextContent);
        Assert.Contains($"{Manager.ColumnCount} of {Manager.Settings.MaxConcurrentWindows} columns", windows.TextContent);
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

        var windows = cut.FindAll(".wm > .wsection")[3];
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
    /// sign-out pages are, it reads from <see cref="XLines.Desk.IDeskAccess"/>.
    /// </summary>
    [Fact]
    public void A_gated_desk_offers_sign_out()
    {
        Services.AddSingleton<XLines.Desk.IDeskAccess>(new GatedAccess(operate: true));

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
        Services.AddSingleton<XLines.Desk.IDeskAccess>(new GatedAccess(operate: false));

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

    private sealed class GatedAccess(bool operate) : XLines.Desk.IDeskAccess
    {
        public bool CanOperate => operate;
        public bool IsGated => true;
        public string? SignInPath => "/in";
        public string? SignOutPath => "/out";
    }
}
