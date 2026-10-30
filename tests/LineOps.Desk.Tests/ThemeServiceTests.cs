using Bunit;
using LineOps.Desk.Theming;

namespace LineOps.Desk.Tests;

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
        string? storedAccent = null)
    {
        _module = JSInterop.SetupModule("./_content/LineOps.Desk/js/theme.js");

        _module.Setup<bool>("prefersDark").SetResult(prefersDark);
        _module.Setup<bool>("prefersMoreContrast").SetResult(moreContrast);
        _module.Setup<bool>("prefersReducedTransparency").SetResult(reducedTransparency);
        _module.SetupVoid("applyTransparency", _ => true).SetVoidResult();
        _module.Setup<string?>("read").SetResult(stored);
        _module.Setup<string?>("readFamily").SetResult(storedFamily);
        _module.SetupVoid("store", _ => true).SetVoidResult();
        _module.SetupVoid("storeFamily", _ => true).SetVoidResult();
        _module.Setup<string?>("readAccent").SetResult(storedAccent);
        _module.SetupVoid("storeAccent", _ => true).SetVoidResult();
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
    [InlineData("Dracula")]
    [InlineData("3")]
    [InlineData("99")]
    public async Task An_unreadable_stored_family_falls_back_to_Standard(string storedFamily)
    {
        var service = Arrange(stored: "Dark", prefersDark: true, storedFamily);

        await service.InitializeAsync();

        Assert.Equal(DeskThemeFamily.Standard, service.Family);
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

        Assert.Equal(DeskAccent.Blue, service.Accent);
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

        Assert.Equal(DeskAccent.Teal, service.Accent);
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

        Assert.Equal(DeskAccent.Purple, service.Accent);
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

        Assert.Equal(DeskAccent.Blue, service.Accent);
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

        Assert.Contains("Graphite", Calls("storeAccent"));
        Assert.Equal(DarkAttribute, LastPaint());
        Assert.Equal("graphite", LastAccent());
        Assert.Equal(1, announced);

        // Neither the mode nor the family was touched: the accent is the finest of the three.
        Assert.Empty(Stored());
        Assert.Empty(Calls("storeFamily"));

        // And back to Blue takes the attribute off again.
        await service.SetAccentAsync(DeskAccent.Blue);

        Assert.Contains("Blue", Calls("storeAccent"));
        Assert.Null(LastAccent());
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
        Assert.Empty(Calls("storeAccent"));
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
