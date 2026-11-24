using Microsoft.JSInterop;

namespace XLines.Desk.Theming;

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
/// <see cref="DeskThemeFamily"/> for why a family is not a fourth appearance. A third, the
/// <see cref="Accent"/>, refines a family: each family offers its own list, the choice is kept
/// per family, and the one spent is the showing family's. The rest belong to every family and
/// change the desk's make rather than its palette: the <see cref="Glass"/> (what the materials are
/// made of), the <see cref="Shape"/> (its corners), the <see cref="Density"/> (its air), the
/// <see cref="Backdrop"/> (its ground) and the <see cref="Typeface"/> (its face).
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

    /// <summary>
    /// The accent key each family was last given, as stored — not yet held to the family's list,
    /// which <see cref="AccentOf"/> does every time it is read. A family with no entry wears its own.
    /// </summary>
    private readonly Dictionary<DeskThemeFamily, string> _accents = [];

    /// <summary>
    /// The showing family's accent: the one chosen for it, or its own until one is and after
    /// anything it does not offer. Each family keeps its own, so an operator who gives Standard
    /// teal, tries Nord and comes back finds their teal waiting — and Nord's choice too, next time.
    /// </summary>
    public DeskFamilyAccent Accent => AccentOf(ShowingFamily);

    /// <summary>
    /// One family's accent, whether or not it is showing: the stored key if the family offers it,
    /// read case-insensitively, and the family's own otherwise.
    /// </summary>
    public DeskFamilyAccent AccentOf(DeskThemeFamily family)
    {
        var info = DeskThemeFamilies.Info(family);

        return _accents.TryGetValue(family, out var key) && info.FindAccent(key) is { } chosen
            ? chosen
            : info.OwnAccent;
    }

    /// <summary>
    /// The value stamped as <c>data-accent</c> on <c>&lt;html&gt;</c>, or null to remove it. Null for
    /// the showing family's own accent, because that is what its blocks already declare and the
    /// inspector should show a choice only when one was made — and an attribute nothing answers
    /// would be a claim the desk is not keeping.
    /// </summary>
    public string? AccentAttribute => AccentAttributeOf(ShowingFamily);

    /// <summary>
    /// What <see cref="AccentAttribute"/> would be were this family showing — what the picker's tile
    /// for it carries, so the tile is painted in the accent choosing it would bring back.
    /// </summary>
    public string? AccentAttributeOf(DeskThemeFamily family)
    {
        var accent = AccentOf(family);

        return accent == DeskThemeFamilies.Info(family).OwnAccent ? null : accent.Key;
    }

    /// <summary>
    /// The position each look (<see cref="DeskLooks"/>) was given; a look with no entry is at its
    /// default. Kept while the machine takes a look off, so the tint an operator chose is waiting
    /// when Reduce Transparency goes off again.
    /// </summary>
    private readonly Dictionary<DeskLook, DeskLookOption> _looks = [];

    /// <summary>One look's position: the one chosen, or its default until one is and after anything unreadable.</summary>
    public DeskLookOption Chosen(DeskLook look)
        => _looks.TryGetValue(look, out var chosen) ? chosen : look.Default;

    /// <summary>
    /// Whether the look is offered: false while the machine setting it yields to is on — under
    /// Reduce Transparency every material is opaque and there is no glass to tint, so the Window
    /// manager does not offer the choice.
    /// </summary>
    public bool Offers(DeskLook look) => look.YieldsTo is null || !MachineSays(look.YieldsTo);

    /// <summary>
    /// The value stamped as the look's attribute on <c>&lt;html&gt;</c>, or null to remove it. Null
    /// for the default, because that is what every family's block already declares and the inspector
    /// should show a choice only when one was made; null while the look is not offered, because the
    /// machine's setting wins there and an attribute nothing answers is a claim the desk is not
    /// keeping. Forced colours draws the plain ground in the stylesheet, whatever is stamped.
    /// </summary>
    public string? AttributeOf(DeskLook look) => Offers(look) ? Chosen(look).Attribute : null;

    /// <summary>
    /// The machine settings a look may yield to, as theme.js reports them: every one of
    /// <see cref="DeskLook.MachineSettings"/>, which a look is held to as it is built, so the
    /// fallback is never reached.
    /// </summary>
    private bool MachineSays(string query) => query switch
    {
        DeskLooks.ReducedTransparency => _systemReducedTransparency,
        _ => false
    };

    /// <summary>
    /// Whether the machine's Reduce Transparency is on. Then every material is opaque and there is
    /// no glass to tint, so the Window manager does not offer <see cref="Glass"/> while it is.
    /// </summary>
    public bool ReducesTransparency => _systemReducedTransparency;

    // The looks by their own types, for callers that know which one they mean. Each is a view of
    // the one table above; the stored name is the enum member's, so the parse cannot fail.

    /// <summary>The operator's glass (<see cref="DeskLooks.Glass"/>). Clear until they choose.</summary>
    public DeskGlass Glass => Typed<DeskGlass>(DeskLooks.Glass);

    /// <summary>The operator's corners (<see cref="DeskLooks.Shape"/>). Standard until they choose.</summary>
    public DeskShape Shape => Typed<DeskShape>(DeskLooks.Shape);

    /// <summary>The operator's air (<see cref="DeskLooks.Density"/>). Standard until they choose.</summary>
    public DeskDensity Density => Typed<DeskDensity>(DeskLooks.Density);

    /// <summary>The operator's ground (<see cref="DeskLooks.Backdrop"/>). Dots, the grid the desk has always drawn, until they choose.</summary>
    public DeskBackdrop Backdrop => Typed<DeskBackdrop>(DeskLooks.Backdrop);

    /// <summary>The operator's face (<see cref="DeskLooks.Typeface"/>). System until they choose.</summary>
    public DeskTypeface Typeface => Typed<DeskTypeface>(DeskLooks.Typeface);

    /// <summary><c>data-glass</c>: "tinted", or null for Clear and under Reduce Transparency (<see cref="AttributeOf"/>).</summary>
    public string? GlassAttribute => AttributeOf(DeskLooks.Glass);

    /// <summary><c>data-shape</c>, by <see cref="AttributeOf"/>.</summary>
    public string? ShapeAttribute => AttributeOf(DeskLooks.Shape);

    /// <summary><c>data-density</c>, by <see cref="AttributeOf"/>.</summary>
    public string? DensityAttribute => AttributeOf(DeskLooks.Density);

    /// <summary><c>data-backdrop</c>, by <see cref="AttributeOf"/>.</summary>
    public string? BackdropAttribute => AttributeOf(DeskLooks.Backdrop);

    /// <summary><c>data-font</c>, by <see cref="AttributeOf"/>.</summary>
    public string? TypefaceAttribute => AttributeOf(DeskLooks.Typeface);

    private T Typed<T>(DeskLook look) where T : struct, Enum => Enum.Parse<T>(Chosen(look).Stored);

    /// <summary>
    /// Whether every appearance setting is where a fresh browser has it: System, no family chosen,
    /// every family in its own accent and every look at its default. The Window manager offers
    /// "Reset appearance" only while this is false.
    /// </summary>
    public bool IsDefaultAppearance
        => Mode == DeskThemeMode.System
           && !_familyChosen
           && DeskThemeFamilies.All.All(family => AccentAttributeOf(family) is null)
           && DeskLooks.All.All(look => Chosen(look) == look.Default);

    /// <summary>
    /// The showing desk's colours, for the charts, which cannot read a token block. It wears the
    /// showing family's chosen accent, so a chart's accent series is the colour of the keys around it.
    /// </summary>
    public DeskPalette Palette
    {
        get
        {
            var palette = DeskThemeFamilies.Palette(ShowingFamily, IsDark);

            return AccentAttribute is not null && Accent.For(IsDark) is { } chosen
                ? palette.WithAccent(chosen)
                : palette;
        }
    }

    /// <summary>
    /// The family's theme for <c>MudThemeProvider</c>, in the family's chosen accent;
    /// <see cref="IsDark"/> picks the palette inside it.
    /// </summary>
    public MudBlazor.MudTheme MudTheme => DeskTheme.For(ShowingFamily, AccentAttribute);

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

        // Whatever is stored is either a mode this version knows, by name, or not a choice at all
        // (TryParseStored, below) — the boot script reads a stored "1" as no choice, and so must this.
        if (TryParseStored<DeskThemeMode>(await module.InvokeAsync<string?>("read"), out var mode))
            Mode = mode;

        // Same rule as the mode, and one the catalogue holds.
        var storedFamily = await module.InvokeAsync<string?>("readFamily");

        if (TryParseStored<DeskThemeFamily>(storedFamily, out var family) && DeskThemeFamilies.All.Contains(family))
        {
            Family = family;
            _familyChosen = true;
        }

        // Every family's accent, in one read, by slug. Kept as stored: AccentOf holds each to its
        // family's list whenever it is read, so a key this version does not offer is simply no
        // choice. Standard's falls back to desk.accent, the key it lived under while the accent was
        // Standard's alone — only when its own is empty, the way an old theme key is read, so a
        // choice made since is never overruled by the one before it.
        var slugs = DeskThemeFamilies.All.Select(DeskThemeFamilies.Slug).ToArray();
        var storedAccents = await module.InvokeAsync<string?[]?>("readAccents", (object)slugs) ?? [];

        for (var i = 0; i < slugs.Length && i < storedAccents.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(storedAccents[i]))
                _accents[DeskThemeFamilies.All[i]] = storedAccents[i]!.Trim();
        }

        if (!_accents.ContainsKey(DeskThemeFamily.Standard))
        {
            var legacy = await module.InvokeAsync<string?>("readAccent");
            if (!string.IsNullOrWhiteSpace(legacy))
                _accents[DeskThemeFamily.Standard] = legacy.Trim();
        }

        // And every look, in one read, by its key — by the family's rule again (DeskLook.Find).
        var lookKeys = DeskLooks.All.Select(look => look.StorageKey).ToArray();
        var storedLooks = await module.InvokeAsync<string?[]?>("readValues", (object)lookKeys) ?? [];

        for (var i = 0; i < lookKeys.Length && i < storedLooks.Length; i++)
        {
            if (DeskLooks.All[i].Find(storedLooks[i]) is { } option)
                _looks[DeskLooks.All[i]] = option;
        }

        _self ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("watch", _self);

        await PaintAsync();
        await module.InvokeVoidAsync("applyTransparency", _systemReducedTransparency);
        await ApplyLooksAsync();
        await NotifyAsync();
    }

    /// <summary>
    /// Whatever is stored is either a member this version defines, by name — trimmed and read
    /// case-insensitively, the boot script's rule — or not a choice at all. <c>Enum.TryParse</c>
    /// alone takes more than a name: "3" as a member, and "Standard, Nord" as the two ORed together,
    /// which is Nord, where the boot script, looking the whole string up, finds no choice and boots
    /// the default — the first paint and the circuit would disagree. So the stored text must be one
    /// member's name and nothing else.
    /// </summary>
    private static bool TryParseStored<T>(string? stored, out T value) where T : struct, Enum
    {
        value = default;

        var wanted = stored?.Trim();
        if (string.IsNullOrEmpty(wanted))
            return false;

        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
        return name is not null && Enum.TryParse(name, out value);
    }

    /// <summary>Records a new choice, persists it, and repaints. A no-op if nothing moved.</summary>
    public async Task SetModeAsync(DeskThemeMode mode)
    {
        if (mode == Mode)
            return;

        var before = Stamped;
        Mode = mode;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("store", mode.ToString());

        await CrossFadeAsync(before, PaintAsync);
    }

    /// <summary>
    /// Records a new family, persists it, and repaints. A no-op if nothing moved — but picking
    /// Standard while it is only the default is a move: it pins Standard against Increase Contrast.
    /// </summary>
    public async Task SetFamilyAsync(DeskThemeFamily family)
    {
        if (family == Family && _familyChosen)
            return;

        var before = Stamped;
        Family = family;
        _familyChosen = true;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("storeFamily", family.ToString());

        await CrossFadeAsync(before, PaintAsync);
    }

    /// <summary>
    /// Records a new accent for the showing family, persists it, and repaints. A no-op if nothing
    /// moved, and for a key the showing family does not offer.
    /// </summary>
    public Task SetAccentAsync(string key) => SetAccentAsync(ShowingFamily, key);

    /// <summary>
    /// Records one of the Standard desk's named accents. Recorded even while another family shows —
    /// a caller that sets it is stating the Standard desk's accent for when it returns.
    /// </summary>
    public Task SetAccentAsync(DeskAccent accent) => SetAccentAsync(DeskThemeFamily.Standard, DeskAccents.Key(accent));

    /// <summary>
    /// Records one family's accent, persists it under that family, and repaints. A no-op if nothing
    /// moved, and for a key the family does not offer. Recorded whether or not the family is
    /// showing; it is stamped only while it is.
    /// </summary>
    public async Task SetAccentAsync(DeskThemeFamily family, string key)
    {
        var accent = DeskThemeFamilies.Info(family).FindAccent(key);

        if (accent is null || accent == AccentOf(family))
            return;

        var before = Stamped;
        _accents[family] = accent.Key;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("storeAccent", DeskThemeFamilies.Slug(family), accent.Key);

        await CrossFadeAsync(before, PaintAsync);
    }

    /// <summary>
    /// Records a look's new position — by its stored name, read as a stored value is — persists it
    /// under the look's key, and restamps every look inside the cross-fade. A no-op if nothing
    /// moved, and for a name the look does not define. Recorded even while the look is not offered
    /// — the glass under Reduce Transparency: the row is hidden then, but a caller that sets it is
    /// stating the look for when the setting goes off.
    /// </summary>
    public async Task SetLookAsync(DeskLook look, string stored)
    {
        var option = look.Find(stored);

        if (option is null || option == Chosen(look))
            return;

        var before = Stamped;
        _looks[look] = option;

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("storeValue", look.StorageKey, option.Stored);

        await CrossFadeAsync(before, ApplyLooksAsync);
    }

    /// <summary>Records a new glass (<see cref="SetLookAsync"/>).</summary>
    public Task SetGlassAsync(DeskGlass glass) => SetLookAsync(DeskLooks.Glass, glass.ToString());

    /// <summary>Records new corners (<see cref="SetLookAsync"/>).</summary>
    public Task SetShapeAsync(DeskShape shape) => SetLookAsync(DeskLooks.Shape, shape.ToString());

    /// <summary>Records a new density (<see cref="SetLookAsync"/>).</summary>
    public Task SetDensityAsync(DeskDensity density) => SetLookAsync(DeskLooks.Density, density.ToString());

    /// <summary>Records a new ground (<see cref="SetLookAsync"/>).</summary>
    public Task SetBackdropAsync(DeskBackdrop backdrop) => SetLookAsync(DeskLooks.Backdrop, backdrop.ToString());

    /// <summary>Records a new face (<see cref="SetLookAsync"/>).</summary>
    public Task SetTypefaceAsync(DeskTypeface typeface) => SetLookAsync(DeskLooks.Typeface, typeface.ToString());

    /// <summary>
    /// Puts every appearance setting back where a fresh browser has it — System, no family chosen
    /// (so Increase Contrast may choose again), every family in its own accent, every look at its
    /// default — forgets every stored choice, and repaints in one cross-fade. The scales are the
    /// Window manager's, kept with the layout, and are not touched.
    /// </summary>
    public async Task ResetAppearanceAsync()
    {
        var before = Stamped;

        Mode = DeskThemeMode.System;
        Family = DeskThemeFamily.Standard;
        _familyChosen = false;
        _accents.Clear();
        _looks.Clear();

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("forgetAppearance",
            DeskThemeFamilies.All.Select(DeskThemeFamilies.Slug).ToArray(),
            DeskLooks.All.Select(look => look.StorageKey).ToArray());

        await CrossFadeAsync(before, async () =>
        {
            await PaintAsync();
            await ApplyLooksAsync();
        });
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
    /// what is showing actually changed. Reduce Transparency moving is always announced, even when
    /// no palette moved: the glass comes off or goes back on with it, and the Window manager's
    /// Glass row is offered only while there is glass to tint.
    /// </para>
    /// </remarks>
    [JSInvokable]
    public async Task OnSystemPreferenceChanged(bool prefersDark, bool moreContrast, bool reducedTransparency)
    {
        var showing = (Attribute, AccentAttribute);
        var transparencyMoved = reducedTransparency != _systemReducedTransparency;

        _systemPrefersDark = prefersDark;
        _systemPrefersMoreContrast = moreContrast;
        _systemReducedTransparency = reducedTransparency;

        if (transparencyMoved)
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("applyTransparency", reducedTransparency);
            await ApplyLooksAsync();
        }

        var repaint = (Attribute, AccentAttribute) != showing;

        if (repaint)
            await PaintAsync();

        if (repaint || transparencyMoved)
            await NotifyAsync();
    }

    /// <summary>Only the appearance moved; the contrast and transparency settings stand as they were.</summary>
    public Task OnSystemPreferenceChanged(bool prefersDark)
        => OnSystemPreferenceChanged(prefersDark, _systemPrefersMoreContrast, _systemReducedTransparency);

    private async Task PaintAsync()
    {
        var module = await ModuleAsync();
        // One call carrying both attributes, so the browser never holds a new accent under the
        // old family (or the reverse) for a frame between two round trips.
        await module.InvokeVoidAsync("apply", Attribute, AccentAttribute);
    }

    /// <summary>
    /// Every look in one call — attribute to value, null to remove — for the reason the family and
    /// accent share one: the browser never holds half a change for a frame between round trips.
    /// </summary>
    private async Task ApplyLooksAsync()
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("applyLooks", LookStamps());
    }

    private Dictionary<string, string?> LookStamps()
        => DeskLooks.All.ToDictionary(look => look.Attribute, AttributeOf);

    /// <summary>
    /// An operator's change, cross-faded: the stamp and the announcement both happen inside one
    /// view transition, so the browser's photograph of the new desk is taken after
    /// MudThemeProvider has re-rendered rather than between the two hops.
    /// </summary>
    /// <remarks>
    /// Only the setters come through here. The first paint and the machine's own change
    /// (<see cref="OnSystemPreferenceChanged(bool, bool, bool)"/>) stay instant: nobody pressed
    /// anything, and a desk that fades by itself at sunset is drawing the eye for no reason.
    /// <para>
    /// <c>beginTransition</c> returns once the old desk is captured (0 when there is no transition
    /// to hold — no API, or reduced motion). The settle is sent without waiting for its reply, on
    /// purpose: the panel that raised this change re-renders only after this method returns, and
    /// a round trip here would hold that render back past the photograph. theme.js lets go once
    /// the document has gone still after the settle, shortly after it whatever still moves, or at
    /// its own limit if it never comes.
    /// </para>
    /// <para>
    /// A choice that moves nothing on <c>&lt;html&gt;</c> — System when the machine already
    /// answers it, an accent for a family not showing, a glass under Reduce Transparency, a reset
    /// of a desk already at its defaults — is stamped and announced without a transition: a
    /// cross-fade from a desk to itself is a page held on its photograph for nothing.
    /// <paramref name="before"/> is <see cref="Stamped"/> read before the choice was recorded.
    /// </para>
    /// </remarks>
    private async Task CrossFadeAsync(StampedLook before, Func<Task> stamp)
    {
        var module = await ModuleAsync();
        var transition = Stamped == before ? 0 : await module.InvokeAsync<int>("beginTransition");

        try
        {
            await stamp();
            await NotifyAsync();
        }
        finally
        {
            if (transition != 0)
                _ = SettleAsync(module, transition);
        }
    }

    private static async Task SettleAsync(IJSObjectReference module, int transition)
    {
        try
        {
            await module.InvokeVoidAsync("settleTransition", transition);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException)
        {
            /* the hold has its own limit in theme.js; nothing is waiting on this — and a module
               disposed with the circuit while the settle was in flight has nothing left to hold */
        }
    }

    /// <summary>Every attribute this service stamps on <c>&lt;html&gt;</c>, as one value a setter can compare.</summary>
    /// <remarks>The looks ride as one string, attribute by attribute, so a look added is compared without a field here.</remarks>
    private readonly record struct StampedLook(string Theme, string? Accent, string Looks);

    private StampedLook Stamped
        => new(Attribute, AccentAttribute, string.Join(';', DeskLooks.All.Select(look => $"{look.Attribute}={AttributeOf(look)}")));

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
        catch (Exception e) when (e is JSDisconnectedException or ObjectDisposedException)
        {
            /* the browser left first, or the module went with it */
        }

        _self?.Dispose();
    }
}
