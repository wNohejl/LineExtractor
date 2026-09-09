using Bunit;
using LineOps.Web.Theming;

namespace LineOps.Web.Tests;

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
    private const string LightAttribute = "light";

    /// <summary>
    /// Stands the module up with a stored choice and a machine preference.
    /// </summary>
    private BunitJSModuleInterop _module = default!;

    private ThemeService Arrange(string? stored, bool prefersDark)
    {
        _module = JSInterop.SetupModule("./js/theme.js");

        _module.Setup<bool>("prefersDark").SetResult(prefersDark);
        _module.Setup<string?>("read").SetResult(stored);
        _module.SetupVoid("store", _ => true).SetVoidResult();
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

    [Fact]
    public async Task Disposal_takes_the_listener_down()
    {
        var service = Arrange(stored: null, prefersDark: true);
        await service.InitializeAsync();

        await service.DisposeAsync();

        Assert.Equal(1, CallCount("unwatch"));
    }
}
