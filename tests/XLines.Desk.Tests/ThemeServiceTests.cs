using System.Reflection;
using Bunit;
using XLines.Desk.Theming;
using Microsoft.JSInterop;

namespace XLines.Desk.Tests;

/// <summary>
/// The theme decision, exercised without a browser.
///
/// <para>
/// Everything worth testing here is the resolution of a three-position setting against a
/// machine preference that can move underneath it, and none of that is browser behaviour —
/// theme.js holds no policy on purpose, precisely so the policy can be tested in one place
/// and one language. The module is stubbed strictly (not with the loose mode the render
/// tests use) because <i>which</i> calls the service makes is part of what is being
/// checked: a mode change that forgets to persist would pass a loose stub silently.
/// </para>
/// </summary>
public class ThemeServiceTests : TestContext
{
    private const string DarkAttribute = "apple-dark";
    private const string LightAttribute = "apple-light";

    /// <summary>
    /// Stands the module up with a stored choice and a machine preference.
    /// </summary>
    private BunitJSModuleInterop _module = default!;

    private ThemeService Arrange(
        string? stored,
        bool prefersDark,
        string? storedFamily = null,
        bool moreContrast = false,
        bool reducedTransparency = false,
        string? storedAccent = null,
        string? storedGlass = null,
        int transition = 1,
        IReadOnlyDictionary<DeskThemeFamily, string>? storedAccents = null,
        IReadOnlyDictionary<string, string>? storedLook = null)
    {
        _module = JSInterop.SetupModule("./_content/XLines.Desk/js/theme.js");

        // beginTransition answers with the transition's id, or 0 where the browser has none to
        // hold (no API, or reduced motion) — theme.js's question, not the service's.
        _module.Setup<int>("beginTransition").SetResult(transition);
        _module.SetupVoid("settleTransition", _ => true).SetVoidResult();

        _module.Setup<bool>("prefersDark").SetResult(prefersDark);
        _module.Setup<bool>("prefersMoreContrast").SetResult(moreContrast);
        _module.Setup<bool>("prefersReducedTransparency").SetResult(reducedTransparency);
        _module.SetupVoid("applyTransparency", _ => true).SetVoidResult();
        _module.Setup<string?>("read").SetResult(stored);
        _module.Setup<string?>("readFamily").SetResult(storedFamily);
        _module.SetupVoid("store", _ => true).SetVoidResult();
        _module.SetupVoid("storeFamily", _ => true).SetVoidResult();
        // storedAccent is the key the accent lived under while it was Standard's alone; the
        // per-family keys answer by slug, in the catalogue's order.
        _module.Setup<string?>("readAccent").SetResult(storedAccent);
        _module.Setup<string?[]?>("readAccents", _ => true).SetResult(DeskThemeFamilies.All
            .Select(f => storedAccents is not null && storedAccents.TryGetValue(f, out var key) ? key : null)
            .ToArray());
        _module.SetupVoid("storeAccent", _ => true).SetVoidResult();
        // Every look answers by its key, written out here rather than read from DeskLooks.All, and
        // only when the service asks for exactly these keys in this order: a key the service got
        // wrong, dropped or reordered finds no handler, and the strict module fails the read.
        // storedGlass is under desk.glass, storedLook by the key's last part (shape, density,
        // backdrop, font).
        string[] lookKeys = ["desk.glass", "desk.shape", "desk.density", "desk.backdrop", "desk.font"];
        _module.Setup<string?[]?>("readValues", call =>
                call.Arguments.Count == 1 && call.Arguments[0] is string[] asked && asked.SequenceEqual(lookKeys))
            .SetResult(lookKeys
                .Select(key => key == "desk.glass" ? storedGlass
                    : storedLook is not null && storedLook.TryGetValue(key["desk.".Length..], out var v) ? v : null)
                .ToArray());
        _module.SetupVoid("storeValue", _ => true).SetVoidResult();
        _module.SetupVoid("applyLooks", _ => true).SetVoidResult();
        _module.SetupVoid("forgetAppearance", _ => true).SetVoidResult();
        _module.SetupVoid("watch", _ => true).SetVoidResult();
        _module.SetupVoid("unwatch").SetVoidResult();
        _module.SetupVoid("apply", _ => true).SetVoidResult();

        return new ThemeService(JSInterop.JSRuntime);
    }

    // Read out of the module's own invocation log rather than off the service, so the
    // assertions are about what the browser was actually told rather than about what was
    // intended. The log lives on the module handler, not the root one: an imported module's
    // calls never appear in JSInterop.Invocations, and a query against the root would return
    // an empty list that Last() turns into a failure and Contains() turns into a pass.
    private string LastPaint() => Calls("apply").Last();

    private IEnumerable<string> Stored() => Calls("store");

    private IEnumerable<string> Calls(string identifier) => _module.Invocations[identifier]
        .Select(i => (string)i.Arguments[0]!);

    private int CallCount(string identifier) => _module.Invocations[identifier].Count;

    /// <summary>What was stored under one key, through storeValue, in order.</summary>
    private IEnumerable<string> StoredUnder(string key) => _module.Invocations["storeValue"]
        .Where(i => (string)i.Arguments[0]! == key)
        .Select(i => (string)i.Arguments[1]!);

    /// <summary>The last look stamp: attribute to value, null where the attribute is removed.</summary>
    private IReadOnlyDictionary<string, string?> LastLooks()
        => (IReadOnlyDictionary<string, string?>)_module.Invocations["applyLooks"].Last().Arguments[0]!;

    [Fact]
    public async Task With_nothing_stored_the_desk_follows_the_machine()
    {
        var service = Arrange(stored: null, prefersDark: false);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeMode.System, service.Mode);
        Assert.False(service.IsDark);
        Assert.Equal(LightAttribute, LastPaint());
    }

    /// <summary>
    /// A stored choice beats the machine outright — that is what choosing means, and a
    /// setting that quietly deferred to prefers-color-scheme would be a setting that did
    /// nothing for exactly the operators who bothered to change it.
    /// </summary>
    [Fact]
    public async Task A_stored_choice_overrides_the_machine()
    {
        var service = Arrange(stored: "Light", prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeMode.Light, service.Mode);
        Assert.False(service.IsDark);
        Assert.Equal(LightAttribute, LastPaint());
    }

    /// <summary>
    /// localStorage holds whatever the last version of the product wrote, plus whatever a
    /// curious operator typed into the console. An unparseable value is not an error state
    /// — it is simply not a choice, so the desk falls back to following the machine.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Sepia")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("2")]
    public async Task An_unreadable_stored_value_falls_back_to_System(string stored)
    {
        var service = Arrange(stored, prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeMode.System, service.Mode);
        Assert.True(service.IsDark);
    }

    [Fact]
    public async Task Choosing_a_mode_persists_it_and_repaints()
    {
        var service = Arrange(stored: null, prefersDark: true);
        await service.InitializeAsync();

        await service.SetModeAsync(DeskThemeMode.Light);

        Assert.Contains("Light", Stored());
        Assert.Equal(LightAttribute, LastPaint());
    }

    /// <summary>
    /// The layout re-renders on this event and nothing else would tell MudThemeProvider to
    /// look again, so an unraised event is a desk whose own tokens have flipped and whose
    /// Mud components have not — the two halves of the product disagreeing on screen.
    /// </summary>
    [Fact]
    public async Task Changing_the_mode_announces_it()
    {
        var service = Arrange(stored: null, prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetModeAsync(DeskThemeMode.Light);

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task Choosing_the_mode_already_showing_does_nothing()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetModeAsync(DeskThemeMode.Dark);

        Assert.Equal(0, announced);
        Assert.Empty(Stored());
    }

    /// <summary>
    /// System has to keep meaning "system". A machine that turns dark at sunset moves the
    /// desk with it, which is the entire difference between three positions and two.
    /// </summary>
    [Fact]
    public async Task Under_System_the_desk_follows_the_machine_changing_its_mind()
    {
        var service = Arrange(stored: null, prefersDark: false);
        await service.InitializeAsync();

        Assert.False(service.IsDark);

        await service.OnSystemPreferenceChanged(prefersDark: true);

        Assert.True(service.IsDark);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    [Fact]
    public async Task A_declared_mode_ignores_the_machine_changing_its_mind()
    {
        var service = Arrange(stored: "Light", prefersDark: false);
        await service.InitializeAsync();

        await service.OnSystemPreferenceChanged(prefersDark: true);

        Assert.False(service.IsDark);
        Assert.Equal(LightAttribute, LastPaint());
    }

    /// <summary>
    /// The subtle one, and the reason OnSystemPreferenceChanged records the preference
    /// before it checks the mode rather than after.
    ///
    /// An operator on Light whose machine goes dark, and who then returns to System, must
    /// land on dark. If the handler had taken an early exit while the mode was Light, the
    /// stored preference would still say "light" from initialisation and System would
    /// resolve to the wrong desk — a bug that only appears in the third step of a sequence
    /// nobody performs by accident.
    /// </summary>
    [Fact]
    public async Task A_preference_that_moved_while_overridden_is_still_remembered()
    {
        var service = Arrange(stored: "Light", prefersDark: false);
        await service.InitializeAsync();

        await service.OnSystemPreferenceChanged(prefersDark: true);
        await service.SetModeAsync(DeskThemeMode.System);

        Assert.True(service.IsDark);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    /// <summary>
    /// The listener is registered exactly once, with the reference disposal will need. A
    /// second registration would double every system-preference repaint.
    /// </summary>
    [Fact]
    public async Task Initialisation_registers_one_system_listener()
    {
        var service = Arrange(stored: null, prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(1, CallCount("watch"));
    }

    // ---- Families ------------------------------------------------------------

    [Fact]
    public async Task With_no_family_stored_the_desk_is_Standard()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Standard, service.Family);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    [Fact]
    public async Task A_stored_family_is_painted_in_the_appearance_showing()
    {
        var service = Arrange(stored: "Light", prefersDark: true, storedFamily: "Nord");

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Nord, service.Family);
        Assert.Equal("nord-light", LastPaint());
        Assert.Same(DeskPalettes.NordLight, service.Palette);
    }

    /// <summary>
    /// The same rule the mode follows: an unreadable value is not a choice. "3" is in the list
    /// because Enum.TryParse accepts a number as a member, and a number is not a name anyone
    /// stored — a stale integer would otherwise select whichever family happens to sit fourth.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Solarised")]
    [InlineData("3")]
    [InlineData("99")]
    public async Task An_unreadable_stored_family_falls_back_to_Standard(string storedFamily)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Standard, service.Family);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    /// <summary>
    /// Enum.TryParse reads a comma list as its members ORed together — "Standard, Nord" is Nord —
    /// where the boot script looks the whole string up and finds no family. The first paint would
    /// be Standard and the circuit would repaint it Nord, so a list is no choice here either; nor is
    /// a sign or a number dressed as one.
    /// </summary>
    [Theory]
    [InlineData("Standard, Nord")]
    [InlineData("Standard,Nord")]
    [InlineData("Nord,")]
    [InlineData(",Nord")]
    [InlineData("+1")]
    [InlineData("-0")]
    public async Task A_stored_family_that_is_not_one_name_is_no_choice(string storedFamily)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Standard, service.Family);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    /// <summary>A name is read as the boot script reads it: trimmed, in any case.</summary>
    [Theory]
    [InlineData("nord")]
    [InlineData("  NORD ")]
    public async Task A_stored_family_is_its_name_trimmed_in_any_case(string storedFamily)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Nord, service.Family);
        Assert.Equal("nord-dark", LastPaint());
    }

    /// <summary>The mode by the same rule: "Light, Dark" is not a mode, and the desk follows the machine.</summary>
    [Theory]
    [InlineData("Light, Dark")]
    [InlineData("System,Light")]
    [InlineData("Light,")]
    public async Task A_stored_mode_that_is_not_one_name_falls_back_to_System(string stored)
    {
        var service = Arrange(stored, prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeMode.System, service.Mode);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    [Fact]
    public async Task Choosing_a_family_persists_it_repaints_and_announces()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetFamilyAsync(DeskThemeFamily.Solarized);

        Assert.Contains("Solarized", Calls("storeFamily"));
        Assert.Equal("solarized-dark", LastPaint());
        Assert.Equal(1, announced);

        // The mode was not touched: a family is the finer choice, not a replacement for it.
        Assert.Empty(Stored());
        Assert.Equal(DeskThemeMode.Dark, service.Mode);
    }

    [Fact]
    public async Task Choosing_the_family_already_showing_does_nothing()
    {
        var service = Arrange(stored: null, prefersDark: true, storedFamily: "RosePine");
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetFamilyAsync(DeskThemeFamily.RosePine);

        Assert.Equal(0, announced);
        Assert.Empty(Calls("storeFamily"));
    }

    /// <summary>
    /// The reason a family is not a fourth appearance. On System, a family keeps following the
    /// machine — the Nord desk turns at sunset exactly as the Standard one does.
    /// </summary>
    [Fact]
    public async Task Under_System_a_family_follows_the_machine_too()
    {
        var service = Arrange(stored: "System", prefersDark: false, storedFamily: "HighContrast");
        await service.InitializeAsync();

        Assert.Equal("contrast-light", LastPaint());

        await service.OnSystemPreferenceChanged(prefersDark: true);

        Assert.Equal("contrast-dark", LastPaint());
        Assert.Same(DeskPalettes.ContrastDark, service.Palette);
    }

    /// <summary>
    /// MudThemeProvider is handed the family's theme and the desk's own tokens come from the
    /// attribute; both are derived from the same two fields, so they cannot disagree.
    /// </summary>
    [Fact]
    public async Task Mud_gets_the_family_the_desk_is_painted_in()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily: "Nord");
        await service.InitializeAsync();

        Assert.Same(DeskTheme.For(DeskThemeFamily.Nord), service.MudTheme);
        Assert.Equal(
            new MudBlazor.Utilities.MudColor(DeskPalettes.NordDark.Accent).Value,
            service.MudTheme.PaletteDark.Primary.Value);
    }

    // ---- The machine's accessibility settings -------------------------------------

    /// <summary>
    /// Increase Contrast is the operator saying, once, for every app, that they need more than
    /// AA. A desk that has never been told which family to wear takes the contrast one while the
    /// setting is on, the same way System takes dark or light, and gives it back when it goes off.
    /// </summary>
    [Fact]
    public async Task More_contrast_switches_an_unpinned_family_to_contrast()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, moreContrast: true);

        await service.InitializeAsync();

        Assert.Equal("contrast-dark", LastPaint());
        Assert.Equal(DeskThemeFamily.HighContrast, service.ShowingFamily);
        Assert.Same(DeskPalettes.ContrastDark, service.Palette);

        // The choice is still unmade: the setting is the operator's, the outcome is the machine's.
        Assert.Equal(DeskThemeFamily.Standard, service.Family);

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: false, reducedTransparency: false);

        Assert.Equal(DarkAttribute, LastPaint());
    }

    [Fact]
    public async Task More_contrast_turning_on_later_repaints_and_announces()
    {
        var service = Arrange(stored: null, prefersDark: false);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.OnSystemPreferenceChanged(prefersDark: false, moreContrast: true, reducedTransparency: false);

        Assert.Equal("contrast-light", LastPaint());
        Assert.Equal(1, announced);
    }

    /// <summary>Someone who chose Nord chose it, and the machine does not get to take it back.</summary>
    [Fact]
    public async Task An_explicit_family_is_kept_under_more_contrast()
    {
        var service = Arrange(stored: "Light", prefersDark: false, storedFamily: "Nord", moreContrast: true);

        await service.InitializeAsync();

        Assert.Equal("nord-light", LastPaint());
        Assert.Equal(DeskThemeFamily.Nord, service.ShowingFamily);
    }

    /// <summary>
    /// Including Standard, chosen on purpose while the setting is on. The family it was merely
    /// defaulting from has to be choosable, so picking it is a choice and not a no-op.
    /// </summary>
    [Fact]
    public async Task Choosing_Standard_under_more_contrast_pins_it()
    {
        var service = Arrange(stored: "Light", prefersDark: false, moreContrast: true);
        await service.InitializeAsync();
        Assert.Equal("contrast-light", LastPaint());

        await service.SetFamilyAsync(DeskThemeFamily.Standard);

        Assert.Contains("Standard", Calls("storeFamily"));
        Assert.Equal(LightAttribute, LastPaint());
    }

    /// <summary>
    /// Reduce Transparency is stamped as an attribute rather than resolved into a family: it
    /// changes what the materials are made of, not which palette they are in, and the stylesheet
    /// answers it. The browser is told on start and again whenever the setting moves.
    /// </summary>
    [Fact]
    public async Task Reduced_transparency_is_stamped_on_start_and_when_it_moves()
    {
        var service = Arrange(stored: null, prefersDark: true, reducedTransparency: true);
        await service.InitializeAsync();

        Assert.True((bool)_module.Invocations["applyTransparency"].Last().Arguments[0]!);

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: false, reducedTransparency: false);

        Assert.False((bool)_module.Invocations["applyTransparency"].Last().Arguments[0]!);
        Assert.Equal(DarkAttribute, LastPaint());
    }

    /// <summary>The one-bool overload means "only the appearance moved"; it does not switch the other two off.</summary>
    [Fact]
    public async Task The_appearance_only_overload_keeps_the_contrast_setting()
    {
        var service = Arrange(stored: null, prefersDark: false, moreContrast: true);
        await service.InitializeAsync();

        await service.OnSystemPreferenceChanged(prefersDark: true);

        Assert.Equal("contrast-dark", LastPaint());
    }

    // ---- The accent ----------------------------------------------------------------

    /// <summary>What the last paint stamped as data-accent: null means the attribute was removed.</summary>
    private string? LastAccent() => (string?)_module.Invocations["apply"].Last().Arguments[1];

    /// <summary>
    /// Blue is the Standard blocks' own accent, so a desk that was never told otherwise removes
    /// the attribute rather than stamping "blue": the inspector shows a choice only when one was made.
    /// </summary>
    [Fact]
    public async Task With_no_accent_stored_the_desk_is_Blue_and_stamps_nothing()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal("blue", service.Accent.Key);
        Assert.Null(service.AccentAttribute);
        Assert.Null(LastAccent());
        Assert.Same(DeskPalettes.AppleDark, service.Palette);
        Assert.Same(DeskTheme.Instance, service.MudTheme);
    }

    [Fact]
    public async Task A_stored_accent_is_stamped_on_the_Standard_desk_and_reaches_Mud_and_the_charts()
    {
        var service = Arrange(stored: "Light", prefersDark: true, storedAccent: "Teal");

        await service.InitializeAsync();

        Assert.Equal("teal", service.Accent.Key);
        Assert.Equal(LightAttribute, LastPaint());
        Assert.Equal("teal", LastAccent());
        Assert.Equal(DeskAccents.Light(DeskAccent.Teal).Accent, service.Palette.Accent);
        Assert.Equal(DeskAccents.Light(DeskAccent.Teal).Hover, service.Palette.AccentHover);
        Assert.Same(DeskTheme.For(DeskThemeFamily.Standard, DeskAccent.Teal), service.MudTheme);
        Assert.Equal(
            new MudBlazor.Utilities.MudColor(DeskAccents.Light(DeskAccent.Teal).Accent).Value,
            service.MudTheme.PaletteLight.Primary.Value);
    }

    /// <summary>
    /// The accent is the Standard family's alone. Under Nord it is kept — an operator who comes
    /// back to Standard finds it waiting — but it is not stamped and nothing wears it.
    /// </summary>
    [Fact]
    public async Task Another_family_keeps_its_own_accent_whatever_is_chosen()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily: "Nord", storedAccent: "Purple");

        await service.InitializeAsync();

        Assert.Equal("purple", service.AccentOf(DeskThemeFamily.Standard).Key);
        Assert.Equal("frost", service.Accent.Key);
        Assert.Equal("nord-dark", LastPaint());
        Assert.Null(LastAccent());
        Assert.Same(DeskPalettes.NordDark, service.Palette);
        Assert.Same(DeskTheme.For(DeskThemeFamily.Nord), service.MudTheme);

        await service.SetFamilyAsync(DeskThemeFamily.Standard);

        Assert.Equal(DarkAttribute, LastPaint());
        Assert.Equal("purple", LastAccent());
    }

    /// <summary>
    /// Increase Contrast on a desk with no family chosen shows High contrast, and High contrast
    /// has no accent to choose — so the chosen one comes off and goes back on with the setting.
    /// </summary>
    [Fact]
    public async Task More_contrast_takes_the_accent_off_while_it_shows_the_contrast_family()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedAccent: "Indigo");
        await service.InitializeAsync();
        Assert.Equal("indigo", LastAccent());

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: true, reducedTransparency: false);

        Assert.Equal("contrast-dark", LastPaint());
        Assert.Null(LastAccent());

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: false, reducedTransparency: false);

        Assert.Equal("indigo", LastAccent());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Orange")]
    [InlineData("2")]
    [InlineData("99")]
    public async Task An_unreadable_stored_accent_falls_back_to_Blue(string storedAccent)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedAccent: storedAccent);

        await service.InitializeAsync();

        Assert.Equal("blue", service.Accent.Key);
        Assert.Null(LastAccent());
    }

    [Fact]
    public async Task Choosing_an_accent_persists_it_repaints_and_announces()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetAccentAsync(DeskAccent.Graphite);

        Assert.Equal(("apple", "graphite"), StoredAccents().Last());
        Assert.Equal(DarkAttribute, LastPaint());
        Assert.Equal("graphite", LastAccent());
        Assert.Equal(1, announced);

        // Neither the mode nor the family was touched: the accent is the finest of the three.
        Assert.Empty(Stored());
        Assert.Empty(Calls("storeFamily"));

        // And back to Blue takes the attribute off again.
        await service.SetAccentAsync("blue");

        Assert.Equal(("apple", "blue"), StoredAccents().Last());
        Assert.Null(LastAccent());
    }

    /// <summary>What storeAccent was handed each time: the family's slug and the key.</summary>
    private IEnumerable<(string Slug, string Key)> StoredAccents() => _module.Invocations["storeAccent"]
        .Select(i => ((string)i.Arguments[0]!, (string)i.Arguments[1]!));

    /// <summary>
    /// Each family keeps its own accent: Standard's is not Nord's, and a trip through another
    /// family brings each one's back. The test family is given a second accent through the
    /// catalogue's own Standard row, the only one that offers more than its own today, so it
    /// reads the per-family key, not the old one.
    /// </summary>
    [Fact]
    public async Task Each_family_keeps_its_own_accent_across_a_switch()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily: "Nord",
            storedAccents: new Dictionary<DeskThemeFamily, string> { [DeskThemeFamily.Standard] = "indigo" });
        await service.InitializeAsync();

        Assert.Equal("frost", service.Accent.Key);
        Assert.Null(LastAccent());
        Assert.Equal("indigo", service.AccentOf(DeskThemeFamily.Standard).Key);
        Assert.Equal("indigo", service.AccentAttributeOf(DeskThemeFamily.Standard));

        await service.SetFamilyAsync(DeskThemeFamily.Standard);
        Assert.Equal("indigo", LastAccent());

        await service.SetAccentAsync("purple");
        await service.SetFamilyAsync(DeskThemeFamily.Nord);
        Assert.Null(LastAccent());

        await service.SetFamilyAsync(DeskThemeFamily.Standard);
        Assert.Equal("purple", LastAccent());
        Assert.Equal(("apple", "purple"), StoredAccents().Single());
    }

    /// <summary>
    /// A key is held to the family it is stored under: Standard's indigo stored under Nord's key
    /// is no choice there, and Nord wears its own.
    /// </summary>
    [Fact]
    public async Task A_stored_key_the_family_does_not_offer_is_its_own_accent()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily: "Nord",
            storedAccents: new Dictionary<DeskThemeFamily, string> { [DeskThemeFamily.Nord] = "indigo" });
        await service.InitializeAsync();

        Assert.Equal("frost", service.Accent.Key);
        Assert.Null(LastAccent());
        Assert.Same(DeskTheme.For(DeskThemeFamily.Nord), service.MudTheme);

        await service.SetAccentAsync("indigo");
        Assert.Empty(_module.Invocations["storeAccent"]);
    }

    /// <summary>
    /// The old key is Standard's fallback, read only while Standard's own key is empty — a choice
    /// made since is never overruled by the one before it.
    /// </summary>
    [Theory]
    [InlineData(null, "Teal", "teal")]
    [InlineData("graphite", "Teal", "graphite")]
    [InlineData("blue", "Teal", null)]
    public async Task The_old_accent_key_is_read_only_while_Standard_s_own_is_empty(string? own, string legacy, string? stamped)
    {
        var accents = own is null ? null : new Dictionary<DeskThemeFamily, string> { [DeskThemeFamily.Standard] = own };
        var service = Arrange(stored: "Dark", prefersDark: true, storedAccent: legacy, storedAccents: accents);

        await service.InitializeAsync();

        Assert.Equal(stamped, LastAccent());
    }

    [Fact]
    public async Task Choosing_the_accent_already_showing_does_nothing()
    {
        var service = Arrange(stored: null, prefersDark: true, storedAccent: "Teal");
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetAccentAsync(DeskAccent.Teal);

        Assert.Equal(0, announced);
        Assert.Empty(_module.Invocations["storeAccent"]);
    }

    // ---- The makes: shape, density, backdrop, typeface -------------------------------

    /// <summary>What the last look stamp sent for the four makes: shape, density, backdrop, font.</summary>
    private (string? Shape, string? Density, string? Backdrop, string? Font) LastLook()
    {
        var looks = LastLooks();
        return (looks["data-shape"], looks["data-density"], looks["data-backdrop"], looks["data-font"]);
    }

    [Fact]
    public async Task With_nothing_stored_every_make_is_its_default_and_stamps_nothing()
    {
        var service = Arrange(stored: null, prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskShape.Standard, service.Shape);
        Assert.Equal(DeskDensity.Standard, service.Density);
        Assert.Equal(DeskBackdrop.Dots, service.Backdrop);
        Assert.Equal(DeskTypeface.System, service.Typeface);
        Assert.Equal((null, null, null, null), LastLook());
        Assert.True(service.IsDefaultAppearance);
    }

    [Fact]
    public async Task Stored_makes_are_stamped_on_start_whatever_family_shows()
    {
        var service = Arrange(stored: "Light", prefersDark: true, storedFamily: "Dracula",
            storedLook: new Dictionary<string, string> { ["shape"] = "Round", ["density"] = "compact", ["backdrop"] = "Grid", ["font"] = "Serif" });

        await service.InitializeAsync();

        Assert.Equal(("round", "compact", "grid", "serif"), LastLook());
        Assert.Equal("dracula-light", LastPaint());
        Assert.False(service.IsDefaultAppearance);
    }

    /// <summary>The family's rule: a name this version defines, or no choice — a number is never a name.</summary>
    [Theory]
    [InlineData("shape", "Square")]
    [InlineData("shape", "1")]
    [InlineData("density", "")]
    [InlineData("density", "2")]
    [InlineData("backdrop", "Waves")]
    [InlineData("backdrop", "0")]
    [InlineData("font", "Comic")]
    [InlineData("font", "3")]
    public async Task An_unreadable_stored_make_falls_back_to_its_default(string name, string stored)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedLook: new Dictionary<string, string> { [name] = stored });

        await service.InitializeAsync();

        Assert.Equal((null, null, null, null), LastLook());
    }

    public static TheoryData<string, string, string> EveryMake() => new()
    {
        { "shape", "Sharp", "sharp" },
        { "shape", "Round", "round" },
        { "density", "Compact", "compact" },
        { "density", "Roomy", "roomy" },
        { "backdrop", "Plain", "plain" },
        { "backdrop", "Gradient", "gradient" },
        { "backdrop", "Grid", "grid" },
        { "font", "Humanist", "humanist" },
        { "font", "Serif", "serif" },
        { "font", "Mono", "mono" }
    };

    private static Task SetMake(ThemeService service, string name, string member) => name switch
    {
        "shape" => service.SetShapeAsync(Enum.Parse<DeskShape>(member)),
        "density" => service.SetDensityAsync(Enum.Parse<DeskDensity>(member)),
        "backdrop" => service.SetBackdropAsync(Enum.Parse<DeskBackdrop>(member)),
        _ => service.SetTypefaceAsync(Enum.Parse<DeskTypeface>(member))
    };

    private static string? Pick((string? Shape, string? Density, string? Backdrop, string? Font) look, string name) => name switch
    {
        "shape" => look.Shape,
        "density" => look.Density,
        "backdrop" => look.Backdrop,
        _ => look.Font
    };

    /// <summary>
    /// Every make, every non-default position: stored under its own name, stamped inside one
    /// cross-fade, announced once, and nothing else touched.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMake))]
    public async Task Choosing_a_make_persists_it_stamps_it_inside_the_cross_fade_and_announces(string name, string member, string stamped)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, transition: 5);
        await service.InitializeAsync();
        var stampsBefore = CallCount("applyLooks");

        var seen = (Opened: 0, Stamped: 0, Settled: -1);
        service.Changed += () =>
        {
            seen = (CallCount("beginTransition"), CallCount("applyLooks") - stampsBefore, CallCount("settleTransition"));
            return Task.CompletedTask;
        };

        await SetMake(service, name, member);

        Assert.Equal((1, 1, 0), seen);
        Assert.Equal(stamped, Pick(LastLook(), name));
        var store = _module.Invocations["storeValue"].Single().Arguments;
        Assert.Equal(("desk." + name, member), ((string)store[0]!, (string)store[1]!));
        Assert.Empty(Stored());
        Assert.Empty(Calls("storeFamily"));
        Assert.Empty(StoredUnder("desk.glass"));
    }

    [Theory]
    [InlineData("shape", "Standard")]
    [InlineData("density", "Standard")]
    [InlineData("backdrop", "Dots")]
    [InlineData("font", "System")]
    public async Task Choosing_the_make_already_showing_does_nothing(string name, string member)
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await SetMake(service, name, member);

        Assert.Equal(0, announced);
        Assert.Empty(_module.Invocations["storeValue"]);
        Assert.Equal(0, CallCount("beginTransition"));
    }

    /// <summary>
    /// The one setter takes a look and a stored name, by the stored-value rule: a name the look
    /// does not define — or a number — moves nothing and stores nothing.
    /// </summary>
    [Theory]
    [InlineData("Square")]
    [InlineData("1")]
    [InlineData("")]
    public async Task A_name_the_look_does_not_define_is_no_choice(string stored)
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        await service.SetLookAsync(DeskLooks.Shape, stored);

        Assert.Empty(_module.Invocations["storeValue"]);
        Assert.Equal(DeskLooks.Shape.Default, service.Chosen(DeskLooks.Shape));
    }

    /// <summary>Every look is stamped in one call, every attribute in it, null for each default.</summary>
    [Fact]
    public async Task Every_look_is_stamped_in_one_call()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedLook: new Dictionary<string, string> { ["density"] = "Roomy" });
        await service.InitializeAsync();

        Assert.Single(_module.Invocations["applyLooks"]);
        Assert.Equal(DeskLooks.All.Select(l => l.Attribute).Order(StringComparer.Ordinal), LastLooks().Keys.Order(StringComparer.Ordinal));
        Assert.Equal("roomy", LastLooks()["data-density"]);
        Assert.Single(LastLooks().Values, v => v is not null);
    }

    /// <summary>A look that yields to the machine is not offered while the machine's setting is on; the rest always are.</summary>
    [Fact]
    public async Task Only_a_look_that_yields_is_taken_off_by_the_machine()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, reducedTransparency: true);
        await service.InitializeAsync();

        Assert.All(DeskLooks.All, look => Assert.Equal(look.YieldsTo is null, service.Offers(look)));
        Assert.False(service.Offers(DeskLooks.Glass));
    }

    // ---- Reset appearance -------------------------------------------------------------

    /// <summary>
    /// Reset puts every appearance setting back where a fresh browser has it, forgets every stored
    /// choice (every family's accent key among them), and repaints in one cross-fade. The family
    /// goes back to unchosen, so Increase Contrast may choose again.
    /// </summary>
    [Fact]
    public async Task Reset_returns_every_appearance_setting_to_its_default_in_one_cross_fade()
    {
        var service = Arrange(stored: "Light", prefersDark: true, storedFamily: "Standard", storedGlass: "Tinted",
            storedAccents: new Dictionary<DeskThemeFamily, string> { [DeskThemeFamily.Standard] = "teal" },
            storedLook: new Dictionary<string, string> { ["shape"] = "Sharp", ["density"] = "Roomy", ["backdrop"] = "Plain", ["font"] = "Mono" },
            transition: 3);
        await service.InitializeAsync();
        Assert.False(service.IsDefaultAppearance);

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.ResetAppearanceAsync();

        Assert.True(service.IsDefaultAppearance);
        Assert.Equal(DeskThemeMode.System, service.Mode);
        Assert.Equal(DarkAttribute, LastPaint());
        Assert.Null(LastAccent());
        Assert.Null(LastGlass());
        Assert.Equal((null, null, null, null), LastLook());
        Assert.Equal(1, announced);
        Assert.Equal(1, CallCount("beginTransition"));
        Assert.Equal(1, CallCount("settleTransition"));

        var forget = _module.Invocations["forgetAppearance"].Single().Arguments;
        Assert.Equal(DeskThemeFamilies.All.Select(DeskThemeFamilies.Slug), (string[])forget[0]!);
        Assert.Equal(DeskLooks.All.Select(look => look.StorageKey), (string[])forget[1]!);

        // Unchosen again: the machine's Increase Contrast may pick the contrast family.
        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: true, reducedTransparency: false);
        Assert.Equal("contrast-dark", LastPaint());
    }

    [Fact]
    public async Task Reset_on_a_default_desk_opens_no_cross_fade()
    {
        var service = Arrange(stored: null, prefersDark: true);
        await service.InitializeAsync();

        await service.ResetAppearanceAsync();

        Assert.Equal(0, CallCount("beginTransition"));
        Assert.Single(_module.Invocations["forgetAppearance"]);
    }

    // ---- The glass -----------------------------------------------------------------

    /// <summary>What the last glass stamp sent: null means the attribute was removed.</summary>
    private string? LastGlass() => LastLooks()["data-glass"];

    /// <summary>
    /// Clear is every family's own material, so a desk that was never told otherwise removes the
    /// attribute rather than stamping "clear": the inspector shows a choice only when one was made.
    /// </summary>
    [Fact]
    public async Task With_no_glass_stored_the_desk_is_Clear_and_stamps_nothing()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);

        await service.InitializeAsync();

        Assert.Equal(DeskGlass.Clear, service.Glass);
        Assert.Null(service.GlassAttribute);
        Assert.Null(LastGlass());
    }

    [Fact]
    public async Task A_stored_tint_is_stamped_on_start_whatever_family_shows()
    {
        var service = Arrange(stored: "Light", prefersDark: true, storedFamily: "Nord", storedGlass: "Tinted");

        await service.InitializeAsync();

        Assert.Equal(DeskGlass.Tinted, service.Glass);
        Assert.Equal("tinted", LastGlass());
        Assert.Equal("nord-light", LastPaint());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Frosted")]
    [InlineData("1")]
    [InlineData("99")]
    public async Task An_unreadable_stored_glass_falls_back_to_Clear(string storedGlass)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedGlass: storedGlass);

        await service.InitializeAsync();

        Assert.Equal(DeskGlass.Clear, service.Glass);
        Assert.Null(LastGlass());
    }

    [Fact]
    public async Task Choosing_a_glass_persists_it_stamps_it_and_announces()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetGlassAsync(DeskGlass.Tinted);

        Assert.Contains("Tinted", StoredUnder("desk.glass"));
        Assert.Equal("tinted", LastGlass());
        Assert.Equal(1, announced);

        // Nothing else was touched: the glass changes the materials, not the palette.
        Assert.Empty(Stored());
        Assert.Empty(Calls("storeFamily"));
        Assert.Empty(Calls("storeAccent"));

        // And back to Clear takes the attribute off again.
        await service.SetGlassAsync(DeskGlass.Clear);

        Assert.Contains("Clear", StoredUnder("desk.glass"));
        Assert.Null(LastGlass());
    }

    [Fact]
    public async Task Choosing_the_glass_already_showing_does_nothing()
    {
        var service = Arrange(stored: null, prefersDark: true, storedGlass: "Tinted");
        await service.InitializeAsync();

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.SetGlassAsync(DeskGlass.Tinted);

        Assert.Equal(0, announced);
        Assert.Empty(StoredUnder("desk.glass"));
    }

    /// <summary>
    /// Reduce Transparency is fully opaque and wins over a tint: the attribute comes off while the
    /// machine's setting is on and goes back on with it, and the choice is kept throughout. The
    /// move is announced even though no palette moved, because the Window manager's row follows it.
    /// </summary>
    [Fact]
    public async Task Reduce_transparency_takes_the_tint_off_and_gives_it_back()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, reducedTransparency: true, storedGlass: "Tinted");
        await service.InitializeAsync();

        Assert.True(service.ReducesTransparency);
        Assert.Equal(DeskGlass.Tinted, service.Glass);
        Assert.Null(LastGlass());

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: false, reducedTransparency: false);

        Assert.False(service.ReducesTransparency);
        Assert.Equal("tinted", LastGlass());
        Assert.Equal(1, announced);

        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: false, reducedTransparency: true);

        Assert.Null(LastGlass());
        Assert.Equal(2, announced);
    }

    // ---- The cross-fade ------------------------------------------------------------

    private static Task Change(ThemeService service, string what) => what switch
    {
        "mode" => service.SetModeAsync(DeskThemeMode.Light),
        "family" => service.SetFamilyAsync(DeskThemeFamily.Nord),
        "accent" => service.SetAccentAsync(DeskAccent.Teal),
        "glass" => service.SetGlassAsync(DeskGlass.Tinted),
        _ => throw new ArgumentOutOfRangeException(nameof(what))
    };

    /// <summary>
    /// The order that keeps the photograph whole: the transition is opened before the stamp, the
    /// stamp and the announcement (MudThemeProvider's re-render) both happen inside it, and only
    /// then is it settled — with the id theme.js handed back, so a later change's hold is not
    /// someone else's to end.
    /// </summary>
    [Theory]
    [InlineData("mode", "apply")]
    [InlineData("family", "apply")]
    [InlineData("accent", "apply")]
    [InlineData("glass", "applyLooks")]
    public async Task An_operator_s_change_is_stamped_and_announced_inside_the_cross_fade(string what, string stamp)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, transition: 7);
        await service.InitializeAsync();
        var stampsBefore = CallCount(stamp);

        var seen = (Opened: 0, Stamped: 0, Settled: -1);
        service.Changed += () =>
        {
            seen = (CallCount("beginTransition"), CallCount(stamp) - stampsBefore, CallCount("settleTransition"));
            return Task.CompletedTask;
        };

        await Change(service, what);

        Assert.Equal((1, 1, 0), seen);
        Assert.Equal(7, (int)_module.Invocations["settleTransition"].Single().Arguments[0]!);
    }

    public static TheoryData<string> EveryLook() => new(DeskLooks.All.Select(look => look.StorageKey));

    /// <summary>
    /// Every look, not only the glass: choosing its last position opens the cross-fade, stamps the
    /// looks and announces inside it, and settles it with theme.js's id.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryLook))]
    public async Task Every_look_s_change_is_stamped_and_announced_inside_the_cross_fade(string key)
    {
        var look = DeskLooks.All.Single(l => l.StorageKey == key);
        var service = Arrange(stored: "Dark", prefersDark: true, transition: 9);
        await service.InitializeAsync();
        var stampsBefore = CallCount("applyLooks");

        var seen = (Opened: 0, Stamped: 0, Settled: -1);
        service.Changed += () =>
        {
            seen = (CallCount("beginTransition"), CallCount("applyLooks") - stampsBefore, CallCount("settleTransition"));
            return Task.CompletedTask;
        };

        await service.SetLookAsync(look, look.Options[^1].Stored);

        Assert.Equal((1, 1, 0), seen);
        Assert.Equal(9, (int)_module.Invocations["settleTransition"].Single().Arguments[0]!);
        Assert.Equal(look.Options[^1].Attribute, LastLooks()[look.Attribute]);
    }

    /// <summary>
    /// Nobody pressed anything at the first paint or at sunset, so neither fades: a desk that
    /// animates by itself draws the eye for no reason. Increase Contrast and Reduce Transparency
    /// moving are the machine's too, and the same.
    /// </summary>
    [Fact]
    public async Task The_first_paint_and_the_machine_s_changes_are_never_cross_faded()
    {
        var service = Arrange(stored: null, prefersDark: false, storedGlass: "Tinted");
        await service.InitializeAsync();

        await service.OnSystemPreferenceChanged(prefersDark: true);
        await service.OnSystemPreferenceChanged(prefersDark: true, moreContrast: true, reducedTransparency: true);

        Assert.Equal("contrast-dark", LastPaint());
        Assert.Equal(0, CallCount("beginTransition"));
        Assert.Equal(0, CallCount("settleTransition"));
    }

    /// <summary>No API, or reduced motion: theme.js answers 0, the switch is the instant one, and there is nothing to settle.</summary>
    [Fact]
    public async Task Without_a_transition_the_change_is_instant_and_nothing_is_settled()
    {
        var service = Arrange(stored: "Dark", prefersDark: true, transition: 0);
        await service.InitializeAsync();

        await service.SetModeAsync(DeskThemeMode.Light);

        Assert.Equal(LightAttribute, LastPaint());
        Assert.Equal(1, CallCount("beginTransition"));
        Assert.Equal(0, CallCount("settleTransition"));
    }

    /// <summary>A handler that throws still lets the transition go, rather than leaving the page held to theme.js's limit.</summary>
    [Fact]
    public async Task A_failed_announcement_still_settles_the_transition()
    {
        var service = Arrange(stored: "Dark", prefersDark: true);
        await service.InitializeAsync();
        service.Changed += () => throw new InvalidOperationException("render failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetModeAsync(DeskThemeMode.Light));

        Assert.Equal(1, CallCount("settleTransition"));
    }

    /// <summary>
    /// A choice that moves nothing on <c>&lt;html&gt;</c> opens no cross-fade — System while the
    /// machine already answers what was showing, an accent while Nord shows, a glass under Reduce
    /// Transparency — or the page is held on a photograph of itself. The choice is still stored,
    /// stamped and announced: it is the operator's, and it shows the moment it can.
    /// </summary>
    [Theory]
    [InlineData("mode", "store")]
    [InlineData("accent", "storeAccent")]
    [InlineData("glass", "storeValue")]
    public async Task A_choice_that_changes_nothing_visible_opens_no_cross_fade(string what, string store)
    {
        var service = what switch
        {
            "accent" => Arrange(stored: "Dark", prefersDark: true, storedFamily: "Nord"),
            "glass" => Arrange(stored: "Dark", prefersDark: true, reducedTransparency: true),
            _ => Arrange(stored: "Dark", prefersDark: true)
        };
        await service.InitializeAsync();
        var showing = (service.Attribute, service.AccentAttribute, service.GlassAttribute);

        var announced = 0;
        service.Changed += () => { announced++; return Task.CompletedTask; };

        await (what switch
        {
            "mode" => service.SetModeAsync(DeskThemeMode.System),
            "accent" => service.SetAccentAsync(DeskAccent.Teal),
            _ => service.SetGlassAsync(DeskGlass.Tinted)
        });

        Assert.Equal(showing, (service.Attribute, service.AccentAttribute, service.GlassAttribute));
        Assert.Equal(0, CallCount("beginTransition"));
        Assert.Equal(0, CallCount("settleTransition"));
        Assert.Single(_module.Invocations[store]);
        Assert.Equal(1, announced);
    }

    /// <summary>
    /// The settle is fire-and-forget, so whatever it throws is thrown into nobody's hands. A
    /// module disposed with the circuit while it was in flight throws ObjectDisposedException,
    /// which is teardown, not a failure, and is swallowed with the disconnect it rides with.
    /// </summary>
    [Theory]
    [InlineData(typeof(ObjectDisposedException))]
    [InlineData(typeof(JSDisconnectedException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task A_settle_into_a_torn_down_module_is_swallowed(Type thrown)
    {
        var settle = typeof(ThemeService).GetMethod("SettleAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var failure = thrown == typeof(ObjectDisposedException)
            ? new ObjectDisposedException("theme.js")
            : (Exception)Activator.CreateInstance(thrown, "gone")!;

        await (Task)settle.Invoke(null, [new ThrowingModule(failure), 7])!;
    }

    private sealed class ThrowingModule(Exception failure) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromException<TValue>(failure);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromException<TValue>(failure);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Disposal_takes_the_listener_down()
    {
        var service = Arrange(stored: null, prefersDark: true);
        await service.InitializeAsync();

        await service.DisposeAsync();

        Assert.Equal(1, CallCount("unwatch"));
    }
}
