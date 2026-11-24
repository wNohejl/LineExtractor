using System.Text.Json;
using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Theming;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The boot script that stamps the stored theme on <c>&lt;html&gt;</c> before the first paint.
/// ThemeService owns the policy; the script is a copy of it in another language, so these tests
/// are what keep the copy reading the same keys and naming the same token blocks. That an
/// application loads it first, through DeskHead, is DeskConventions.AppHeadLoadsDeskFirst.
/// </summary>
public class ThemeBootTests
{
    private static readonly string Boot = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "theme-boot.js"));
    private static readonly string Theme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "theme.js"));

    /// <summary>The tables DeskHead writes ahead of the boot script, parsed.</summary>
    private static JsonElement Tables() => JsonDocument.Parse(DeskBoot.Json).RootElement;

    // The stored value is the enum member's name, read case-insensitively as ThemeService reads it:
    // the boot tables key every family by that name in lower case.
    [Theory]
    [MemberData(nameof(DeskThemeTests.EveryFamily), MemberType = typeof(DeskThemeTests))]
    public void The_boot_tables_read_every_family_by_its_stored_name(DeskThemeFamily family)
        => Assert.Equal(DeskThemeFamilies.Slug(family),
            Tables().GetProperty("families").GetProperty(family.ToString().ToLowerInvariant()).GetString());

    /// <summary>
    /// The script carries no table of its own any more: no family's slug and no accent key appears
    /// in it as a literal, save the default family's once, the desk a host with no tables boots.
    /// </summary>
    [Fact]
    public void The_boot_script_names_no_family_and_no_accent()
    {
        var code = Regex.Replace(Boot, @"//[^\n]*", "");
        var standard = DeskThemeFamilies.Slug(DeskThemeFamily.Standard);
        var literals = Regex.Matches(code, @"'([^'\n]*)'").Select(m => m.Groups[1].Value).ToList();

        foreach (var info in DeskThemeFamilies.Catalog)
        {
            if (info.Slug != standard)
                Assert.DoesNotContain(info.Slug, literals);
            foreach (var accent in info.Accents)
                Assert.DoesNotContain(accent.Key, literals);
        }

        Assert.Single(literals, l => l == standard);
        Assert.Matches($@"const DEFAULT_SLUG = '{standard}';", Boot);
        foreach (var look in DeskLooks.All)
        {
            Assert.DoesNotContain(look.StorageKey, literals);
            Assert.DoesNotContain(look.Attribute, literals);
        }
    }

    /// <summary>
    /// The script reads the tables by DeskBoot's id, and a page without them — an older host's head,
    /// or a block that does not parse — still boots: the plain desk, by the stored mode.
    /// </summary>
    [Fact]
    public void The_boot_script_reads_the_tables_and_boots_without_them()
    {
        Assert.Contains($"document.getElementById('{DeskBoot.ElementId}')", Boot);
        Assert.Matches(@"JSON\.parse\(block\.textContent \|\| ''\)", Boot);
        Assert.Matches(@"\} catch \{\s*return \{\};\s*\}", Boot);
        Assert.Contains("typeof tables.standard === 'string' ? tables.standard : DEFAULT_SLUG", Boot);
    }

    /// <summary>
    /// The tables are written into the head as a data block, never executed, and escaped so nothing
    /// in them can close the element: no angle bracket or ampersand survives the encoder.
    /// </summary>
    [Fact]
    public void The_boot_tables_cannot_break_out_of_their_element()
    {
        Assert.DoesNotContain("<", DeskBoot.Json);
        Assert.DoesNotContain(">", DeskBoot.Json);
        Assert.DoesNotContain("&", DeskBoot.Json);
        Assert.Same(DeskBoot.Json, DeskBoot.Json);
    }

    /// <summary>The rendered head carries the tables whole, as a data block, ahead of the boot script.</summary>
    [Fact]
    public void DeskHead_renders_the_boot_tables_whole()
    {
        using var context = new HeadContext();
        var head = context.RenderComponent<XLines.Desk.Layout.DeskHead>();

        var block = head.Find($"script#{DeskBoot.ElementId}");
        Assert.Equal("application/json", block.GetAttribute("type"));
        Assert.Equal(DeskBoot.Json, block.TextContent);
        Assert.True(JsonDocument.Parse(block.TextContent).RootElement.TryGetProperty("looks", out _));

        var scripts = head.FindAll("script").ToList();
        Assert.True(scripts.IndexOf(block) < scripts.FindIndex(s => (s.GetAttribute("src") ?? "").EndsWith("theme-boot.js", StringComparison.Ordinal)));
    }

    private sealed class HeadContext : DeskTestContext;

    /// <summary>DeskHead writes the block, by DeskBoot's id, before the script that reads it and before any stylesheet.</summary>
    [Fact]
    public void DeskHead_writes_the_boot_tables_ahead_of_the_boot_script()
    {
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "DeskHead.razor"));
        var block = head.IndexOf("<script type=\"application/json\" id=\"@DeskBoot.ElementId\">@((MarkupString)DeskBoot.Json)</script>", StringComparison.Ordinal);

        Assert.True(block >= 0, "DeskHead.razor writes no boot tables.");
        Assert.True(block < head.IndexOf("theme-boot.js\")", StringComparison.Ordinal));
    }

    [Fact]
    public void The_boot_script_reads_the_same_keys_as_theme_js()
    {
        var keys = Regex.Matches(Theme, @"const \w*KEY = '([^']+)'").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.Contains($"'{key}'", Boot);
    }

    // The keys carry the Desk's name now, and a browser that stored its theme under the old one
    // must still come up in it: both scripts know both keys, the new one is read first, and the
    // old one is only ever read.
    [Theory]
    [InlineData("desk.theme")]
    [InlineData("desk.theme.family")]
    [InlineData("lineops.theme")]
    [InlineData("lineops.theme.family")]
    public void Both_scripts_know_the_desk_s_key_and_the_one_before_it(string key)
    {
        Assert.Contains($"'{key}'", Theme);
        Assert.Contains($"'{key}'", Boot);
    }

    [Theory]
    [InlineData("desk.theme", "lineops.theme")]
    [InlineData("desk.theme.family", "lineops.theme.family")]
    public void The_boot_script_reads_the_new_key_and_falls_back_to_the_old(string key, string old)
        => Assert.Matches($@"stored\('{Regex.Escape(key)}'\)\s*\|\|\s*stored\('{Regex.Escape(old)}'\)", Boot);

    [Fact]
    public void Theme_js_reads_the_new_key_before_the_old_and_writes_only_the_new()
    {
        Assert.Matches(@"const KEY = 'desk\.theme'", Theme);
        Assert.Matches(@"const FAMILY_KEY = 'desk\.theme\.family'", Theme);
        Assert.Matches(@"readKey\(KEY, OLD_KEY\)", Theme);
        Assert.Matches(@"readKey\(FAMILY_KEY, OLD_FAMILY_KEY\)", Theme);
        Assert.DoesNotMatch(@"setItem\(OLD_", Theme);
    }

    /// <summary>
    /// A stored value of nothing but whitespace is no value in both scripts: the boot script trims
    /// what it reads and falls back to the old key, so theme.js's readKey treats a blank new key as
    /// empty, falls back the same way, and copies forward only a value that is not blank.
    /// </summary>
    [Fact]
    public void Both_scripts_take_a_blank_stored_value_for_none()
    {
        Assert.Contains("(localStorage.getItem(key) || '').trim()", Boot);

        var start = Theme.IndexOf("function readKey(", StringComparison.Ordinal);
        Assert.True(start >= 0, "theme.js has no readKey.");
        var readKey = Theme[start..Theme.IndexOf("\nexport ", start, StringComparison.Ordinal)];

        Assert.Matches(@"if \(!blank\(value\)\) \{\s*return value;", readKey);
        Assert.Matches(@"if \(blank\(before\)\) \{\s*return null;\s*\}\s*localStorage\.setItem\(key, before\);", readKey);
        Assert.Matches(@"function blank\(value\) \{\s*return value === null \|\| value\.trim\(\) === '';", readKey);
    }

    // ---- The pickers' keys -------------------------------------------------------------

    /// <summary>
    /// A roving radiogroup's arrows, Home and End are held from scrolling the page it sits in —
    /// exactly the keys Roving answers, so Tab and everything else pass — on the radios of a group
    /// marked data-roving alone, and the listener goes with the watch. Each of these groups answers
    /// its keys through Roving and carries the marker; theme.js names none of them.
    /// </summary>
    [Theory]
    [InlineData("DeskThemePicker.razor")]
    [InlineData("DeskAccentPicker.razor")]
    [InlineData("DeskRadioGroup.razor")]
    public void Theme_js_holds_exactly_the_keys_each_roving_group_handles(string picker)
    {
        var held = Regex.Match(Theme, @"const CHOICE_KEYS = new Set\(\[([^\]]*)\]\);");
        Assert.True(held.Success, "theme.js declares no CHOICE_KEYS.");
        var keys = Regex.Matches(held.Groups[1].Value, @"'(\w+)'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(Roving.Keys.Order(StringComparer.Ordinal), keys);
        Assert.DoesNotContain("Tab", keys);
        Assert.Contains($"const CHOICE_RADIOS = '[role=\"radiogroup\"][{Roving.Marker}] [role=\"radio\"]';", Theme);
        Assert.DoesNotContain(".themes[", Theme);
        Assert.DoesNotContain(".accents[", Theme);
        Assert.Contains("document.addEventListener('keydown', holdChoiceKeys);", Function(Theme, "watch"));
        Assert.Contains("document.removeEventListener('keydown', holdChoiceKeys);", Function(Theme, "unwatch"));

        var razor = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Primitives", picker));
        Assert.Matches(@"role=""radiogroup""\s[^>]*\bdata-roving\b", razor);
        Assert.Contains("Roving.Next(", razor);
        Assert.DoesNotContain("\"ArrowRight\"", razor);
    }

    // ---- The accent ----------------------------------------------------------------

    /// <summary>
    /// The boot tables' families are the catalogue's, no more and no fewer: every row by its
    /// member's name and slug, and the two slugs a desk wears unchosen.
    /// </summary>
    [Fact]
    public void The_boot_tables_families_are_the_catalogue_s()
    {
        var expected = DeskThemeFamilies.Catalog.ToDictionary(f => f.Family.ToString().ToLowerInvariant(), f => f.Slug);
        var families = Tables().GetProperty("families").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

        Assert.Equal(expected.OrderBy(p => p.Key), families.OrderBy(p => p.Key));
        Assert.Equal(DeskThemeFamilies.Slug(DeskThemeFamily.Standard), Tables().GetProperty("standard").GetString());
        Assert.Equal(DeskThemeFamilies.Slug(DeskThemeFamily.HighContrast), Tables().GetProperty("contrast").GetString());
    }

    /// <summary>
    /// The boot tables' accents are the catalogue's: for every family, every key but its own first
    /// one, in order, and an entry only for a family that offers more than its own. The C# row and
    /// <c>DeskThemeTests.Every_family_s_accents_are_its_blocks</c> are the two halves an integrator
    /// adds together; this table is built from the first.
    /// </summary>
    [Fact]
    public void The_boot_tables_accents_are_the_catalogue_s()
    {
        var expected = DeskThemeFamilies.Catalog
            .Where(f => f.Accents.Count > 1)
            .ToDictionary(f => f.Slug, f => f.Accents.Skip(1).Select(a => a.Key).ToArray());
        var boot = Tables().GetProperty("accents").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(k => k.GetString()!).ToArray());

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), boot.Keys.Order(StringComparer.Ordinal));
        foreach (var (slug, keys) in expected)
            Assert.Equal(keys, boot[slug]);
    }

    /// <summary>
    /// Each family's choice lives under its own key — desk.accent.&lt;slug&gt; in both scripts —
    /// and Standard's falls back to desk.accent only while its own is empty, as ThemeService reads it.
    /// </summary>
    [Fact]
    public void Both_scripts_keep_each_family_s_accent_under_its_own_key()
    {
        Assert.Matches(@"const FAMILY_ACCENT_KEY = 'desk\.accent\.'", Theme);
        Assert.Matches(@"const ACCENT_KEY = 'desk\.accent'", Theme);
        Assert.Matches(@"localStorage\.setItem\(FAMILY_ACCENT_KEY \+ slug, accent\)", Theme);
        Assert.DoesNotMatch(@"setItem\(ACCENT_KEY", Theme);

        Assert.Contains(
            "stored('desk.accent.' + slug) || (slug === STANDARD ? stored('desk.accent') : '')",
            Boot);
    }

    /// <summary>
    /// The accent is stamped only when it is one the resolved family has a block for — the same
    /// question ThemeService.AccentAttribute asks — and in one place.
    /// </summary>
    [Fact]
    public void The_boot_script_stamps_only_an_accent_the_resolved_family_has_a_block_for()
    {
        Assert.Matches(@"const accents = own\(ACCENTS, slug\) && Array\.isArray\(ACCENTS\[slug\]\) \? ACCENTS\[slug\] : \[\];\s*if \(accents\.indexOf\(accent\) >= 0\) \{\s*root\.dataset\.accent = accent;", Boot);
        Assert.Single(Regex.Matches(Boot, @"dataset\.accent\s*="));
    }

    // ---- The looks -------------------------------------------------------------------

    public static TheoryData<string> EveryLook() => new(DeskLooks.All.Select(look => look.StorageKey));

    /// <summary>
    /// Every look is in the boot tables by the key ThemeService stores it under and the attribute it
    /// stamps, with exactly the values it stamps — every position but the default, which is what
    /// every block already declares — and the machine setting that takes it off.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryLook))]
    public void The_boot_tables_restore_every_look_ThemeService_stamps(string key)
    {
        var look = Assert.Single(DeskLooks.All, l => l.StorageKey == key);
        var row = Assert.Single(Tables().GetProperty("looks").EnumerateArray(), r => r.GetProperty("key").GetString() == key);

        Assert.Equal(look.Attribute, row.GetProperty("attribute").GetString());
        Assert.Equal(
            look.Options.Where(o => o.Attribute is not null).ToDictionary(o => o.Stored.ToLowerInvariant(), o => o.Attribute),
            row.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()));
        Assert.Equal(look.YieldsTo, row.GetProperty("yieldsTo").GetString());
    }

    // ---- The emitted JSON, by value ----------------------------------------------------
    //
    // The tests above rebuild the expected tables from the catalogue the way DeskBoot does, so a
    // slip in both — a slug built from the wrong field, a default that stamps — would agree with
    // itself. These read the emitted JSON against values written out by hand.

    [Theory]
    [InlineData("standard", "apple")]
    [InlineData("highcontrast", "contrast")]
    [InlineData("tokyonight", "tokyo-night")]
    [InlineData("rosepine", "rose-pine")]
    [InlineData("nightowl", "night-owl")]
    [InlineData("nord", "nord")]
    public void The_boot_tables_name_a_family_s_slug_by_its_stored_name(string stored, string slug)
        => Assert.Equal(slug, Tables().GetProperty("families").GetProperty(stored).GetString());

    [Fact]
    public void The_boot_tables_name_the_unchosen_and_the_contrast_desk()
    {
        Assert.Equal("apple", Tables().GetProperty("standard").GetString());
        Assert.Equal("contrast", Tables().GetProperty("contrast").GetString());
        Assert.False(Tables().GetProperty("families").TryGetProperty("tokyo-night", out _), "a family is keyed by its stored name, not its slug");
    }

    [Fact]
    public void The_boot_tables_list_a_family_s_accents_but_its_own_and_none_for_high_contrast()
    {
        var accents = Tables().GetProperty("accents");
        string[] Keys(string slug) => accents.GetProperty(slug).EnumerateArray().Select(k => k.GetString()!).ToArray();

        Assert.Equal(new[] { "teal", "purple" }, Keys("nord"));
        Assert.Equal(new[] { "teal", "indigo", "purple", "graphite" }, Keys("apple"));
        Assert.False(accents.TryGetProperty("contrast", out _), "High contrast offers only its own accent");
    }

    [Fact]
    public void The_boot_tables_carry_each_look_by_value()
    {
        var looks = Tables().GetProperty("looks").EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!);
        Dictionary<string, string?> Values(string key) => looks[key].GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());

        Assert.Equal(new[] { "desk.glass", "desk.shape", "desk.density", "desk.backdrop", "desk.font" }, looks.Keys);

        var backdrop = Values("desk.backdrop");
        Assert.Equal("data-backdrop", looks["desk.backdrop"].GetProperty("attribute").GetString());
        Assert.Equal("grid", backdrop["grid"]);
        Assert.Equal("gradient", backdrop["gradient"]);
        Assert.Equal("plain", backdrop["plain"]);
        Assert.False(backdrop.ContainsKey("dots"), "the default backdrop stamps nothing");

        Assert.Equal(new Dictionary<string, string?> { ["tinted"] = "tinted" }, Values("desk.glass"));
        Assert.Equal("(prefers-reduced-transparency: reduce)", looks["desk.glass"].GetProperty("yieldsTo").GetString());
        Assert.Equal(JsonValueKind.Null, looks["desk.shape"].GetProperty("yieldsTo").ValueKind);
        Assert.Equal("data-font", looks["desk.font"].GetProperty("attribute").GetString());
        Assert.False(Values("desk.font").ContainsKey("system"));
        Assert.False(Values("desk.shape").ContainsKey("standard"));
        Assert.False(Values("desk.density").ContainsKey("standard"));
    }

    [Fact]
    public void The_boot_tables_hold_every_look_and_nothing_else()
        => Assert.Equal(DeskLooks.All.Select(l => l.StorageKey),
            Tables().GetProperty("looks").EnumerateArray().Select(r => r.GetProperty("key").GetString()));

    /// <summary>
    /// The boot script stamps a look in one place, only for a value the tables give it and never
    /// while the machine setting it yields to is on; theme.js stamps every look in one call and
    /// removes what is sent as null.
    /// </summary>
    [Fact]
    public void The_boot_script_stamps_the_looks_in_one_place_and_theme_js_removes_what_is_not_sent()
    {
        Assert.Single(Regex.Matches(Boot, @"root\.setAttribute\(look\.attribute, values\[value\]\)"));
        Assert.Contains("if (own(values, value) && !(look.yieldsTo && matches(look.yieldsTo, false))) {", Boot);
        Assert.Matches(@"export function applyLooks\(looks\)", Theme);
        Assert.Contains("root.removeAttribute(name)", Function(Theme, "applyLooks"));
        Assert.DoesNotContain("'data-glass'", Theme);
        Assert.DoesNotContain("'data-shape'", Theme);
    }

    /// <summary>
    /// A reset forgets every key theme.js writes, the old ones included, so none is copied forward
    /// again — and every look's key, which ThemeService hands it.
    /// </summary>
    [Fact]
    public void Reset_forgets_every_key_theme_js_writes()
    {
        var forget = Function(Theme, "forgetAppearance");

        foreach (var constant in Regex.Matches(Theme, @"const (\w*KEY) = '").Select(m => m.Groups[1].Value).Where(c => c != "FAMILY_ACCENT_KEY"))
            Assert.True(forget.Contains(constant), $"forgetAppearance does not forget {constant}");

        Assert.Matches(@"export function forgetAppearance\(slugs, looks\)", Theme);
        Assert.Contains("FAMILY_ACCENT_KEY + slug", forget);
        Assert.Contains(".concat(looks || [])", forget);
        Assert.Contains("localStorage.removeItem(key)", forget);
    }

    [Fact]
    public void Theme_js_removes_the_accent_attribute_when_none_is_sent()
    {
        Assert.Matches(@"export function apply\(theme, accent\)", Theme);
        Assert.Contains("removeAttribute('data-accent')", Theme);
    }

    // ---- The glass -----------------------------------------------------------------

    /// <summary>
    /// The glass keeps the key and the value it always had, so a browser that chose a tint before
    /// the looks were one table comes up tinted; the boot tables stamp it only as "tinted", never
    /// Clear, and only while the machine is not reducing transparency — the questions
    /// ThemeService.GlassAttribute asks, and the query theme.js watches.
    /// </summary>
    [Fact]
    public void The_glass_is_kept_under_its_key_and_yields_to_reduce_transparency()
    {
        Assert.Equal("desk.glass", DeskLooks.Glass.StorageKey);
        Assert.Equal("data-glass", DeskLooks.Glass.Attribute);
        Assert.Equal(DeskLooks.ReducedTransparency, DeskLooks.Glass.YieldsTo);
        Assert.Contains($"'{DeskLooks.ReducedTransparency}'", Theme);

        var row = Tables().GetProperty("looks").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "desk.glass");
        Assert.Equal(new[] { "tinted" }, row.GetProperty("values").EnumerateObject().Select(p => p.Name));
        Assert.Equal("tinted", row.GetProperty("values").GetProperty("tinted").GetString());
        Assert.DoesNotContain("'clear'", Boot);
    }

    /// <summary>
    /// The four makes yield to nothing — forced colours draws the plain ground in the stylesheet —
    /// and keep the keys and attributes they always had.
    /// </summary>
    [Theory]
    [InlineData("desk.shape", "data-shape")]
    [InlineData("desk.density", "data-density")]
    [InlineData("desk.backdrop", "data-backdrop")]
    [InlineData("desk.font", "data-font")]
    public void The_makes_keep_their_keys_and_yield_to_nothing(string key, string attribute)
    {
        var look = Assert.Single(DeskLooks.All, l => l.StorageKey == key);

        Assert.Equal(attribute, look.Attribute);
        Assert.Null(look.YieldsTo);
    }

    /// <summary>
    /// The tint is derived from whatever family is showing, so it names none: no rule keyed on a
    /// [data-theme] value, and it keeps the blur — Reduce Transparency's block is the only one that
    /// takes it away, and it outweighs this one wherever both are stamped.
    /// </summary>
    [Fact]
    public void The_tinted_glass_is_derived_for_every_family_and_keeps_its_blur()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        var rules = Regex.Matches(DeskConventions.WithoutComments(css), @"([^{}]*\[data-glass=""tinted""\][^{}]*)\{([^}]*)\}")
            .ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.DoesNotContain("data-theme", rule.Groups[1].Value, StringComparison.Ordinal);
            Assert.DoesNotContain("--material-blur", rule.Groups[2].Value, StringComparison.Ordinal);
        });

        var body = string.Concat(rules.Select(r => r.Groups[2].Value));
        foreach (var tier in new[] { "ultrathin", "thin", "regular", "thick" })
            Assert.Matches($@"--material-{tier}:\s*color-mix\(in srgb, var\(--glass-clear-{tier}\), var\(--surface-2\)", body);
    }

    // ---- The browser's chrome --------------------------------------------------------

    /// <summary>
    /// theme-color is read back from the showing block's ground in both scripts, never kept as a
    /// palette of its own: no colour literal in either file, and both ask for --surface-0.
    /// </summary>
    [Fact]
    public void Both_scripts_take_theme_color_from_the_showing_ground_and_hold_no_palette()
    {
        foreach (var script in new[] { Boot, Theme })
        {
            Assert.Contains("getPropertyValue('--surface-0')", script);
            Assert.Contains("meta[name=\"theme-color\"]", script);
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgba?\(", script);
        }
    }

    /// <summary>Every stamp repaints the chrome: the attribute first, then the colour read from it.</summary>
    [Fact]
    public void Theme_js_repaints_theme_color_after_each_stamp()
        => Assert.Matches(@"export function apply\(theme, accent\) \{[^}]*setAttribute\('data-theme', theme\)(?s:.*?)stampThemeColor\(\);\s*\}", Theme);

    /// <summary>
    /// The meta is the Desk's to declare, in its half of the head, ahead of the boot script that
    /// fills it — so a host's App.razor carries none of its own and needs no edit.
    /// </summary>
    [Fact]
    public void DeskHead_declares_theme_color_before_the_boot_script()
    {
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "DeskHead.razor"));
        var meta = head.IndexOf("<meta name=\"theme-color\"", StringComparison.Ordinal);

        Assert.True(meta >= 0, "DeskHead.razor declares no theme-color.");
        Assert.True(meta < head.IndexOf("theme-boot.js\")", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"theme-color""\s+content=""[^""]+""", head);
    }

    // ---- The cross-fade --------------------------------------------------------------

    private static string Function(string script, string name)
    {
        var start = script.IndexOf($"export function {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"theme.js exports no {name}.");
        var next = script.IndexOf("\nexport ", start + 1, StringComparison.Ordinal);
        return next < 0 ? script[start..] : script[start..next];
    }

    /// <summary>
    /// Feature-detected, and declined under reduced motion before any photograph is taken; both
    /// answer 0, which is ThemeService's "nothing to settle".
    /// </summary>
    [Fact]
    public void The_cross_fade_is_feature_detected_and_declined_under_reduced_motion()
    {
        var begin = Function(Theme, "beginTransition");

        Assert.Matches(@"if \(typeof document\.startViewTransition !== 'function' \|\| reducesMotion\(\)\) \{\s*return Promise\.resolve\(0\);", begin);
        Assert.Contains("'(prefers-reduced-motion: reduce)'", Theme);
    }

    /// <summary>
    /// A second change skips the first transition, whose promises then reject with AbortError —
    /// all three are caught, and the first hold is ended by the second rather than left open.
    /// </summary>
    [Fact]
    public void A_skipped_transition_neither_throws_nor_stays_held()
    {
        var begin = Function(Theme, "beginTransition");

        foreach (var promise in new[] { "ready", "updateCallbackDone", "finished" })
            Assert.Contains($"quiet(transition.{promise});", begin);

        Assert.Matches(@"if \(held\) \{\s*held\.end\(\);\s*\}\s*held = hold\(id, done\);", begin);
        Assert.Matches(@"if \(held && held\.id === id\)", Function(Theme, "settleTransition"));
    }

    /// <summary>
    /// Every wait is bounded, well inside the four seconds a browser allows an update callback, so
    /// a lost circuit or a settle that never comes cannot freeze the page on its photograph.
    /// </summary>
    [Fact]
    public void The_hold_is_bounded()
    {
        var limit = Regex.Match(Theme, @"const HOLD_LIMIT = (\d+);");

        Assert.True(limit.Success, "theme.js declares no HOLD_LIMIT.");
        Assert.InRange(int.Parse(limit.Groups[1].Value), 1, 2000);
        Assert.Contains("setTimeout(end, HOLD_LIMIT)", Theme);
    }

    /// <summary>
    /// A live desk is never still — a ticking label, a streamed row — so a hold that waited only
    /// for stillness ran every switch to HOLD_LIMIT with rendering paused (review, 2026-11-21).
    /// Once settled, the hold lets go within a short SETTLE_LIMIT whatever keeps moving, started
    /// by the first settle and not restarted by a mutation or a second one, and cleared with the
    /// rest when the hold ends.
    /// </summary>
    [Fact]
    public void A_settled_hold_lets_go_shortly_even_while_the_document_keeps_moving()
    {
        var settleLimit = Regex.Match(Theme, @"const SETTLE_LIMIT = (\d+);");
        var holdLimit = Regex.Match(Theme, @"const HOLD_LIMIT = (\d+);");

        Assert.True(settleLimit.Success, "theme.js declares no SETTLE_LIMIT.");
        Assert.InRange(int.Parse(settleLimit.Groups[1].Value), 100, 250);
        Assert.True(int.Parse(settleLimit.Groups[1].Value) < int.Parse(holdLimit.Groups[1].Value));

        var start = Theme.IndexOf("function hold(", StringComparison.Ordinal);
        Assert.True(start >= 0, "theme.js has no hold().");
        var hold = Theme[start..Theme.IndexOf("export function beginTransition(", start, StringComparison.Ordinal)];
        var at = hold.IndexOf("function settle(", StringComparison.Ordinal);
        Assert.True(at >= 0, "hold() has no settle().");
        var settle = hold[at..hold.IndexOf("return { id", at, StringComparison.Ordinal)];

        Assert.Matches(@"if \(!settleTimer\) \{\s*settleTimer = setTimeout\(end, SETTLE_LIMIT\);\s*\}", settle);
        Assert.Matches(@"function end\(\) \{[^}]*clearTimeout\(settleTimer\);", hold);

        // The observer's callback only ever moves the quiet timer, never the settle one.
        var observer = Regex.Match(hold, @"new MutationObserver\(\(\) => \{([^}]*)\}\)");
        Assert.True(observer.Success, "hold() has no MutationObserver.");
        Assert.DoesNotContain("settleTimer", observer.Groups[1].Value);
    }

    /// <summary>
    /// The instant stamps stay instant — the first paint and the machine's own changes go through
    /// them — and the boot script, which runs before anything can be photographed, never fades.
    /// </summary>
    [Fact]
    public void Only_beginTransition_opens_a_transition()
    {
        Assert.Single(Regex.Matches(Theme, @"startViewTransition\("));
        Assert.Contains("startViewTransition(", Function(Theme, "beginTransition"));
        Assert.DoesNotContain("startViewTransition", Boot);
    }

    [Fact]
    public void DeskHead_loads_the_boot_script_before_any_stylesheet()
    {
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "Layout", "DeskHead.razor"));
        var boot = head.IndexOf("theme-boot.js\")", StringComparison.Ordinal);

        Assert.True(boot >= 0, "DeskHead.razor does not load theme-boot.js.");
        Assert.True(boot < head.IndexOf("<link rel=\"stylesheet\"", StringComparison.Ordinal));
    }
}
