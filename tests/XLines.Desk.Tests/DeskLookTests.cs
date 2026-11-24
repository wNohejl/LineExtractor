using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using XLines.Desk.Theming;
using Microsoft.AspNetCore.Components.Web;

namespace XLines.Desk.Tests;

/// <summary>
/// The looks table (<see cref="DeskLooks"/>) is the one place an appearance setting is declared, so
/// these hold each row to the three things it must reach: a gate on the Window manager
/// (WindowManagerControlsTests.Every_look_is_a_gate_in_the_appearance_group_in_its_order), an entry
/// in the boot tables (ThemeBootTests.The_boot_tables_restore_every_look_ThemeService_stamps) and a
/// block in the stylesheet for every position it stamps (here).
/// </summary>
public class DeskLookTests
{
    private static readonly string Css = string.Concat(
        Directory.GetFiles(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css"), "*.css")
            .Select(path => DeskConventions.WithoutComments(File.ReadAllText(path))));

    public static TheoryData<string> EveryLook() => new(DeskLooks.All.Select(look => look.StorageKey));

    private static DeskLook Look(string key) => DeskLooks.All.Single(look => look.StorageKey == key);

    /// <summary>
    /// A row is well formed: a key, an attribute, at least two positions with distinct names and
    /// labels, exactly one that stamps nothing and it first — the default leads its gate — and every
    /// other a distinct value.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryLook))]
    public void Every_look_is_well_formed(string key)
    {
        var look = Look(key);

        Assert.StartsWith("desk.", look.StorageKey);
        Assert.StartsWith("data-", look.Attribute);
        Assert.True(look.Options.Count >= 2, $"{look.Label} offers no choice.");
        Assert.Null(look.Options[0].Attribute);
        Assert.Single(look.Options, o => o.Attribute is null);
        Assert.Same(look.Options[0], look.Default);
        Assert.Equal(look.Options.Count, look.Options.Select(o => o.Stored.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(look.Options.Count, look.Options.Select(o => o.Label).Distinct().Count());
        Assert.Equal(look.Options.Count - 1, look.Options.Where(o => o.Attribute is not null).Select(o => o.Attribute).Distinct().Count());
    }

    [Fact]
    public void No_two_looks_share_a_key_an_attribute_or_a_label()
    {
        Assert.Equal(DeskLooks.All.Count, DeskLooks.All.Select(l => l.StorageKey).Distinct().Count());
        Assert.Equal(DeskLooks.All.Count, DeskLooks.All.Select(l => l.Attribute).Distinct().Count());
        Assert.Equal(DeskLooks.All.Count, DeskLooks.All.Select(l => l.Label).Distinct().Count());
    }

    /// <summary>Every position a look stamps has a block in the Desk's stylesheets that answers it.</summary>
    [Theory]
    [MemberData(nameof(EveryLook))]
    public void Every_stamped_position_has_a_block_in_the_stylesheet(string key)
    {
        var look = Look(key);

        foreach (var option in look.Options.Where(o => o.Attribute is not null))
            Assert.True(Css.Contains($"[{look.Attribute}=\"{option.Attribute}\"]", StringComparison.Ordinal),
                $"No stylesheet answers [{look.Attribute}=\"{option.Attribute}\"].");
    }

    /// <summary>
    /// A look over an enum stores each member by its name, stamps it by its name in lower case, and
    /// offers every member — so the typed members ThemeService keeps (Glass, Shape, …) can always
    /// read the table back.
    /// </summary>
    [Fact]
    public void The_typed_looks_mirror_their_enums()
    {
        AssertMirrors<DeskGlass>(DeskLooks.Glass, DeskGlass.Clear);
        AssertMirrors<DeskShape>(DeskLooks.Shape, DeskShape.Standard);
        AssertMirrors<DeskDensity>(DeskLooks.Density, DeskDensity.Standard);
        AssertMirrors<DeskBackdrop>(DeskLooks.Backdrop, DeskBackdrop.Dots);
        AssertMirrors<DeskTypeface>(DeskLooks.Typeface, DeskTypeface.System);

        static void AssertMirrors<T>(DeskLook look, T byDefault) where T : struct, Enum
        {
            Assert.Equal(Enum.GetNames<T>().Order(StringComparer.Ordinal), look.Options.Select(o => o.Stored).Order(StringComparer.Ordinal));
            Assert.Equal(byDefault.ToString(), look.Default.Stored);
            Assert.All(look.Options.Where(o => o.Attribute is not null), o => Assert.Equal(o.Stored.ToLowerInvariant(), o.Attribute));
        }
    }

    /// <summary>The family's rule for a stored value: a name, read case-insensitively and trimmed, or no choice — a number never.</summary>
    [Theory]
    [InlineData("Round", "Round")]
    [InlineData("round", "Round")]
    [InlineData("  SHARP ", "Sharp")]
    [InlineData("Standard", "Standard")]
    [InlineData("Square", null)]
    [InlineData("1", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_stored_position_is_found_by_its_name_or_not_at_all(string? stored, string? found)
        => Assert.Equal(found, DeskLooks.Shape.Find(stored)?.Stored);

    // ---- The machine settings a look yields to ---------------------------------------

    /// <summary>
    /// A look may yield only to a machine setting the desk watches and answers: one theme.js
    /// watches (QUERIES) and ThemeService reads. Anything else throws as the row is built, rather
    /// than taking the look off at the first paint and handing it back when the circuit opens.
    /// </summary>
    [Theory]
    [InlineData("(prefers-contrast: more)")]
    [InlineData("(prefers-color-scheme: dark)")]
    [InlineData("(prefers-reduced-motion: reduce)")]
    [InlineData("")]
    public void A_look_cannot_yield_to_a_setting_the_desk_does_not_answer(string query)
    {
        var options = new List<DeskLookOption> { new("Off", null, "Off"), new("On", "on", "On") };

        var thrown = Assert.Throws<ArgumentException>(() => new DeskLook("Sample", "desk.sample", "data-sample", options, query));
        Assert.Equal("YieldsTo", thrown.ParamName);
    }

    [Fact]
    public void A_look_may_yield_to_reduce_transparency_or_to_nothing()
    {
        var options = new List<DeskLookOption> { new("Off", null, "Off"), new("On", "on", "On") };

        Assert.Equal(DeskLooks.ReducedTransparency, new DeskLook("Sample", "desk.sample", "data-sample", options, DeskLooks.ReducedTransparency).YieldsTo);
        Assert.Null(new DeskLook("Sample", "desk.sample", "data-sample", options).YieldsTo);
        Assert.Equal(new[] { "(prefers-reduced-transparency: reduce)" }, DeskLook.MachineSettings);
    }

    /// <summary>Every machine setting a look may yield to is one theme.js watches, so its change reaches ThemeService.</summary>
    [Fact]
    public void Every_machine_setting_is_watched_by_theme_js()
    {
        var theme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "theme.js"));
        var queries = System.Text.RegularExpressions.Regex.Match(theme, @"const QUERIES = \[([^\]]*)\];");
        Assert.True(queries.Success, "theme.js declares no QUERIES.");

        Assert.All(DeskLook.MachineSettings, setting => Assert.Contains($"'{setting}'", queries.Groups[1].Value));
        Assert.All(DeskLooks.All.Where(l => l.YieldsTo is not null), look => Assert.Contains(look.YieldsTo, DeskLook.MachineSettings));
    }

    // ---- Roving ---------------------------------------------------------------------

    [Theory]
    [InlineData(0, 3, "ArrowRight", false, 1)]
    [InlineData(0, 3, "ArrowDown", false, 1)]
    [InlineData(2, 3, "ArrowRight", false, 2)]
    [InlineData(2, 3, "ArrowRight", true, 0)]
    [InlineData(1, 3, "ArrowLeft", false, 0)]
    [InlineData(1, 3, "ArrowUp", false, 0)]
    [InlineData(0, 3, "ArrowLeft", false, 0)]
    [InlineData(0, 3, "ArrowLeft", true, 2)]
    [InlineData(1, 3, "Home", false, 0)]
    [InlineData(1, 3, "End", false, 2)]
    [InlineData(1, 3, "Home", true, 0)]
    [InlineData(1, 3, "End", true, 2)]
    [InlineData(0, 1, "ArrowRight", true, 0)]
    public void Roving_moves_the_choice_by_the_radiogroup_contract(int current, int count, string key, bool wrap, int expected)
        => Assert.Equal(expected, Roving.Next(current, count, key, wrap));

    [Theory]
    [InlineData("Tab")]
    [InlineData("Enter")]
    [InlineData(" ")]
    [InlineData("a")]
    public void Roving_leaves_every_other_key_alone(string key)
        => Assert.Null(Roving.Next(1, 3, key, wrap: false));

    [Fact]
    public void Roving_has_nothing_to_move_in_an_empty_group()
        => Assert.Null(Roving.Next(0, 0, "ArrowRight", wrap: true));

    [Fact]
    public void Roving_answers_exactly_its_keys()
        => Assert.All(Roving.Keys, key => Assert.NotNull(Roving.Next(0, 2, key, wrap: false)));

    /// <summary>
    /// A key with Ctrl, Alt or Meta held is the browser's — Alt+ArrowLeft is Back, Ctrl+Home the
    /// top of the page — and theme.js does not hold it, so the group must not move under it either:
    /// the choice would change, and be stored, on the way to another page.
    /// </summary>
    [Theory]
    [InlineData("ArrowLeft", true, false, false)]
    [InlineData("ArrowRight", false, true, false)]
    [InlineData("Home", true, false, false)]
    [InlineData("End", false, false, true)]
    [InlineData("ArrowDown", true, true, true)]
    public void Roving_leaves_a_key_with_a_modifier_to_the_browser(string key, bool ctrl, bool alt, bool meta)
        => Assert.Null(Roving.Next(1, 3, new KeyboardEventArgs { Key = key, CtrlKey = ctrl, AltKey = alt, MetaKey = meta }, wrap: true));

    /// <summary>Shift alone is still the group's, as theme.js holds a shifted arrow from the page.</summary>
    [Theory]
    [InlineData("ArrowRight", 2)]
    [InlineData("Home", 0)]
    public void Roving_answers_a_shifted_key_as_it_answers_the_key(string key, int expected)
        => Assert.Equal(expected, Roving.Next(1, 3, new KeyboardEventArgs { Key = key, ShiftKey = true }, wrap: false));

    /// <summary>The C# side ignores exactly the modifiers theme.js lets through, and Shift is neither.</summary>
    [Fact]
    public void Roving_and_theme_js_pass_the_same_modifiers()
    {
        var theme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "theme.js"));

        Assert.Contains("if (!CHOICE_KEYS.has(e.key) || e.ctrlKey || e.metaKey || e.altKey) {", theme);
        Assert.DoesNotContain("e.shiftKey", theme);
        Assert.False(Roving.IsShortcut(new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true }));
    }
}
