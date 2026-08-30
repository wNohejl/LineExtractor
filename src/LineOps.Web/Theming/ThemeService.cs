using Microsoft.JSInterop;

namespace LineOps.Web.Theming;

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
/// The two consumers want different things and both are served from <see cref="IsDark"/>.
/// The desk's own components read the <c>[data-theme]</c> attribute this stamps onto
/// <c>&lt;html&gt;</c> — that is the token block switching, and it is the whole mechanism.
/// MudBlazor cannot read a token block, so <c>MudThemeProvider.IsDarkMode</c> is bound to
/// the same boolean; the two are never computed separately, because a desk whose Mud
/// components disagree with its own panels is worse than either theme alone.
/// </para>
/// </summary>
public sealed class ThemeService : IAsyncDisposable
{
    private const string ModulePath = "./js/theme.js";

    /// <summary>The value stamped on <c>&lt;html&gt;</c>. Must match the selectors in lineops.css.</summary>
    internal const string DarkTheme = "apple-dark";

    internal const string LightTheme = "light";

    private readonly IJSRuntime _js;

    private IJSObjectReference? _module;
    private DotNetObjectReference<ThemeService>? _self;

    /// <summary>
    /// What the machine last said. Seeded on the first render and kept current by the
    /// matchMedia listener, so <see cref="DeskThemeMode.System"/> stays a live answer.
    /// </summary>
    private bool _systemPrefersDark = true;

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

    /// <summary>
    /// Raised whenever <see cref="IsDark"/> may have moved. The layout re-renders on it,
    /// because <c>MudThemeProvider</c> takes its mode as a parameter rather than reading
    /// it back, and nothing else would tell it to look again.
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

        var stored = await module.InvokeAsync<string?>("read");

        if (Enum.TryParse<DeskThemeMode>(stored, ignoreCase: true, out var mode))
            Mode = mode;

        _self ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("watch", _self);

        await PaintAsync();
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
    /// The machine changed its mind — sunset, or a settings toggle. Called from theme.js.
    /// </summary>
    /// <remarks>
    /// The preference is recorded whatever the mode, rather than only under
    /// <see cref="DeskThemeMode.System"/>: an operator sitting on <see cref="DeskThemeMode.Light"/>
    /// whose machine goes dark and who then switches back to System must land on dark, and
    /// they will not if this method took an early exit and left the flag stale.
    /// </remarks>
    [JSInvokable]
    public async Task OnSystemPreferenceChanged(bool prefersDark)
    {
        if (prefersDark == _systemPrefersDark)
            return;

        _systemPrefersDark = prefersDark;

        if (Mode != DeskThemeMode.System)
            return;

        await PaintAsync();
        await NotifyAsync();
    }

    private async Task PaintAsync()
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("apply", IsDark ? DarkTheme : LightTheme);
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
