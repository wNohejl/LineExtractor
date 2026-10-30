using Microsoft.JSInterop;

namespace LineOps.Desk.Theming;

/// <summary>
/// Which desk is showing, and the one place that decides it.
///
/// <para>
/// Scoped, for the same reason <c>WindowManager</c> and <c>DeskToasts</c> are: the DOM
/// this writes to and the <c>localStorage</c> it reads from belong to one circuit, and a
/// singleton would hand every operator on the server the last one's choice.
/// </para>
///
/// <para>
/// A theme is two choices: the <see cref="Family"/> — which palette — and the <see cref="Mode"/>
/// — dark, light, or whatever the machine says. They are independent on purpose; see
/// <see cref="DeskThemeFamily"/> for why a family is not a fourth appearance.
/// </para>
///
/// <para>
/// The two consumers want different things and both are served from the same pair. The
/// desk's own components read the <c>[data-theme]</c> attribute this stamps onto
/// <c>&lt;html&gt;</c> — that is the token block switching, and it is the whole mechanism.
/// MudBlazor cannot read a token block, so <c>MudThemeProvider</c> is handed
/// <see cref="MudTheme"/> and <see cref="IsDark"/>, resolved from the same two fields; they are
/// never computed separately, because a desk whose Mud components disagree with its own panels
/// is worse than either theme alone.
/// </para>
/// </summary>
public sealed class ThemeService : IAsyncDisposable
{
    private const string ModulePath = $"./{DeskAssets.Root}/js/theme.js";

    private readonly IJSRuntime _js;

    private IJSObjectReference? _module;
    private DotNetObjectReference<ThemeService>? _self;

    /// <summary>
    /// What the machine last said. Seeded on the first render and kept current by the
    /// matchMedia listener, so <see cref="DeskThemeMode.System"/> stays a live answer.
    /// </summary>
    private bool _systemPrefersDark = true;

    /// <summary>
    /// The machine's Increase Contrast setting (<c>prefers-contrast: more</c>). Kept current by
    /// the same listener; it decides the family only while the operator has not chosen one.
    /// </summary>
    private bool _systemPrefersMoreContrast;

    /// <summary>
    /// The machine's Reduce Transparency setting. It does not pick a palette; it is stamped as
    /// <c>data-reduced-transparency</c> and the stylesheet makes every material opaque under it.
    /// </summary>
    private bool _systemReducedTransparency;

    /// <summary>
    /// Whether <see cref="Family"/> is a choice or only the default. A readable stored family
    /// is a choice, and so is any pick from the picker — Standard included.
    /// </summary>
    private bool _familyChosen;

    public ThemeService(IJSRuntime js) => _js = js;

    /// <summary>The operator's choice — the setting, not the outcome.</summary>
    public DeskThemeMode Mode { get; private set; } = DeskThemeMode.System;

    /// <summary>
    /// The outcome — the setting resolved against the machine. This is what both
    /// <c>MudThemeProvider</c> and the <c>[data-theme]</c> attribute are driven from.
    /// </summary>
    public bool IsDark => Mode switch
    {
        DeskThemeMode.Dark => true,
        DeskThemeMode.Light => false,
        _ => _systemPrefersDark
    };

    /// <summary>The operator's palette. Standard until they choose, and after anything unreadable.</summary>
    public DeskThemeFamily Family { get; private set; } = DeskThemeFamily.Standard;

    /// <summary>
    /// The palette showing — the family resolved against the machine, as <see cref="IsDark"/> is
    /// the mode resolved against it. Under Increase Contrast a desk that was never told which
    /// family to wear wears the contrast one, the way System wears dark at night; a family the
    /// operator chose is theirs, and the machine does not overrule it.
    /// </summary>
    public DeskThemeFamily ShowingFamily
        => !_familyChosen && _systemPrefersMoreContrast ? DeskThemeFamily.HighContrast : Family;

    /// <summary>The value on <c>&lt;html&gt;</c>, and the selector of the token block that is showing.</summary>
    public string Attribute => DeskThemeFamilies.Attribute(ShowingFamily, IsDark);

    /// <summary>The showing desk's colours, for the charts, which cannot read a token block.</summary>
    public DeskPalette Palette => DeskThemeFamilies.Palette(ShowingFamily, IsDark);

    /// <summary>The family's theme for <c>MudThemeProvider</c>; <see cref="IsDark"/> picks the palette inside it.</summary>
    public MudBlazor.MudTheme MudTheme => DeskTheme.For(ShowingFamily);

    /// <summary>
    /// Raised whenever the showing desk may have moved — its family or its appearance. The
    /// layout re-renders on it, because <c>MudThemeProvider</c> takes its theme and mode as
    /// parameters rather than reading them back, and nothing else would tell it to look again.
    /// </summary>
    public event Func<Task>? Changed;

    /// <summary>
    /// Reads the stored choice and the machine's preference, then paints.
    ///
    /// <para>
    /// Called from the layout's first <c>OnAfterRenderAsync</c> and nowhere else. It
    /// cannot run earlier: there is no JS runtime during prerender, so a constructor or
    /// <c>OnInitializedAsync</c> that reached for <c>localStorage</c> would throw on
    /// every first paint. The cost is one frame of the default theme before the stored
    /// one lands, which is why the default is the dark desk the product already was —
    /// an operator who has never chosen sees no flash at all, and one who chose light
    /// sees a single frame rather than a whole session of the wrong desk.
    /// </para>
    /// </summary>
    public async Task InitializeAsync()
    {
        var module = await ModuleAsync();

        _systemPrefersDark = await module.InvokeAsync<bool>("prefersDark");
        _systemPrefersMoreContrast = await module.InvokeAsync<bool>("prefersMoreContrast");
        _systemReducedTransparency = await module.InvokeAsync<bool>("prefersReducedTransparency");

        var stored = await module.InvokeAsync<string?>("read");

        if (Enum.TryParse<DeskThemeMode>(stored, ignoreCase: true, out var mode))
            Mode = mode;

        // Same rule as the mode: whatever is stored is either a family this version knows or
        // not a choice at all. Enum.TryParse alone would take "3" as a family, so the name has
        // to be one the enum actually defines.
        var storedFamily = await module.InvokeAsync<string?>("readFamily");

        if (Enum.TryParse<DeskThemeFamily>(storedFamily, ignoreCase: true, out var family)
            && Enum.IsDefined(family)
            && !int.TryParse(storedFamily, out _))
        {
            Family = family;
            _familyChosen = true;
        }

        _self ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("watch", _self);

        await PaintAsync();
        await module.InvokeVoidAsync("applyTransparency", _systemReducedTransparency);
        await NotifyAsync();
    }

    /// <summary>Records a new choice, persists it, and repaints. A no-op if nothing moved.</summary>
    public async Task SetModeAsync(DeskThemeMode mode)
    {
        if (mode == Mode)
            return;

        Mode = mode;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("store", mode.ToString());

        await PaintAsync();
        await NotifyAsync();
    }

    /// <summary>
    /// Records a new family, persists it, and repaints. A no-op if nothing moved — but picking
    /// Standard while it is only the default is a move: it pins Standard against Increase Contrast.
    /// </summary>
    public async Task SetFamilyAsync(DeskThemeFamily family)
    {
        if (family == Family && _familyChosen)
            return;

        Family = family;
        _familyChosen = true;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("storeFamily", family.ToString());

        await PaintAsync();
        await NotifyAsync();
    }

    /// <summary>
    /// The machine changed its mind — sunset, or a settings toggle. Called from theme.js.
    /// </summary>
    /// <remarks>
    /// The preference is recorded whatever the mode, rather than only under
    /// <see cref="DeskThemeMode.System"/>: an operator sitting on <see cref="DeskThemeMode.Light"/>
    /// whose machine goes dark and who then switches back to System must land on dark, and
    /// they will not if this method took an early exit and left the flag stale.
    /// <para>
    /// The same rule holds for Increase Contrast and Reduce Transparency: theme.js reports all
    /// three whenever any one moves, every one is recorded, and the desk is repainted only if
    /// what is showing actually changed.
    /// </para>
    /// </remarks>
    [JSInvokable]
    public async Task OnSystemPreferenceChanged(bool prefersDark, bool moreContrast, bool reducedTransparency)
    {
        var showing = Attribute;
        var transparencyMoved = reducedTransparency != _systemReducedTransparency;

        _systemPrefersDark = prefersDark;
        _systemPrefersMoreContrast = moreContrast;
        _systemReducedTransparency = reducedTransparency;

        if (transparencyMoved)
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("applyTransparency", reducedTransparency);
        }

        if (Attribute == showing)
            return;

        await PaintAsync();
        await NotifyAsync();
    }

    /// <summary>Only the appearance moved; the contrast and transparency settings stand as they were.</summary>
    public Task OnSystemPreferenceChanged(bool prefersDark)
        => OnSystemPreferenceChanged(prefersDark, _systemPrefersMoreContrast, _systemReducedTransparency);

    private async Task PaintAsync()
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("apply", Attribute);
    }

    private Task NotifyAsync() => Changed?.Invoke() ?? Task.CompletedTask;

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    /// <summary>
    /// Tears the matchMedia listener down with the circuit.
    /// </summary>
    /// <remarks>
    /// Both calls are wrapped: disposal routinely runs <i>after</i> the circuit has gone,
    /// and reaching for JS then throws <see cref="JSDisconnectedException"/> — the documented
    /// shape of "the browser is already gone", not an error worth surfacing. There is nothing
    /// left to clean up in that case anyway, because the page holding the listener is closed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("unwatch");
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            /* the browser left first */
        }

        _self?.Dispose();
    }
}
