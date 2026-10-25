namespace LineOps.Desk.Windowing;

/// <summary>Whether the desk sizes itself to the browser or to a resolution the operator set.</summary>
public enum ResolutionMode
{
    /// <summary>Follow the browser window.</summary>
    Auto,

    /// <summary>Use <see cref="DeskSettings.CustomWidth"/>/<see cref="DeskSettings.CustomHeight"/>.</summary>
    Fixed
}

/// <summary>
/// Operator-controlled desk configuration, edited in the Window manager window.
///
/// There is no layout setting: the desk is always a horizontal row of full-height columns,
/// and every window is its own tab in that row — there is no separate taskbar to configure.
/// Freedom to place things arbitrarily belongs to modals and popovers layered above the
/// desk, not to the desk itself.
/// </summary>
public class DeskSettings
{
    /// <summary>
    /// How many windows may be open at once. Reaching it does not refuse the next window —
    /// the least recently used one is closed to make room, and the footer says which.
    /// </summary>
    public int MaxConcurrentWindows { get; set; } = 4;

    /// <summary>
    /// The window that opens on an empty desk and takes the larger share of the row.
    /// Null means every column is equal. This only affects width — it does not pin the
    /// window's position, so dragging it to reorder the row is never fought by a default.
    /// </summary>
    public string? PrimaryWindowKey { get; set; }

    /// <summary>Fraction of the row the primary window takes when more than one is open.</summary>
    public double PrimaryShare { get; set; } = 0.45;

    public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Auto;
    public int CustomWidth { get; set; } = 1920;
    public int CustomHeight { get; set; } = 1080;

    /// <summary>
    /// The whole desk's scale — type, spacing, controls, chrome — as a multiplier. Spent as
    /// CSS zoom on the page, so the desk lays out in fewer, larger pixels: at 1.25 a 1600px
    /// browser is a 1280px desk, and <see cref="WindowManager.RecommendedCapacity"/> says so.
    /// </summary>
    public double UiScale { get; set; } = 1;

    /// <summary>Type only, as a multiplier. The layout keeps its geometry and the words grow inside it.</summary>
    public double TextScale { get; set; } = 1;

    public const double MinUiScale = 0.8;
    public const double MaxUiScale = 1.5;
    public const double MinTextScale = 0.9;
    public const double MaxTextScale = 1.4;

    /// <summary>
    /// A scale as it may be stored: inside its range, and 1 for anything that is not a number
    /// at all — which is what a layout saved before scale existed reads as, since its missing
    /// field deserialises to 0.
    /// </summary>
    public static double ClampScale(double value, double min, double max)
        => double.IsFinite(value) && value > 0 ? Math.Clamp(value, min, max) : 1;

    /// <summary>Narrowest a column may become when the operator drags a divider.</summary>
    public const int MinColumnWidth = 260;

    /// <summary>Fixed width of a collapsed (minimised) tab — icon and pulse only.</summary>
    public const int CollapsedWidth = 46;
}

/// <summary>
/// Live state of a window, surfaced on its title bar and readable even collapsed.
///
/// This is the point of the whole layout: an operator watching one window needs to know
/// something changed in another without switching to it. Hue therefore always encodes
/// state and never decorates.
/// </summary>
public enum PulseState
{
    /// <summary>Nothing to report.</summary>
    Idle,

    /// <summary>Working — a fetch or evaluation is in flight.</summary>
    Active,

    /// <summary>Within SLO.</summary>
    Healthy,

    /// <summary>Degraded but serving.</summary>
    Warn,

    /// <summary>Breached. Needs someone.</summary>
    Critical
}

/// <summary>
/// A window that can be opened. Registered once in the application's <see cref="IWindowCatalog"/>; the
/// component type is resolved at render time, so adding a window is a catalog entry
/// rather than a change to the host.
/// </summary>
public record WindowDefinition
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required Type ComponentType { get; init; }

    /// <summary>Grouping in the launcher — Data, Analytics, Operations, System.</summary>
    public string Group { get; init; } = "Data";

    /// <summary>Relative width in the row when nothing else has been adjusted.</summary>
    public double DefaultWeight { get; init; } = 1;

    public int MinWidth { get; init; } = 300;

    /// <summary>
    /// When true, opening again focuses the existing window instead of making a second one.
    /// Dashboards are singletons; a line-movement chart is not — two side by side is the point.
    /// </summary>
    public bool Singleton { get; init; } = true;

    /// <summary>
    /// Whether this window is about something, and so cannot be opened on its own.
    ///
    /// <para>
    /// A game, a team and a player view are destinations: they take an id, and a window built
    /// without one has nothing to show. Opened cold from the launcher they sat on "Loading…"
    /// for ever, which reads as a broken window rather than as a window that was never meant to
    /// be opened that way.
    /// </para>
    ///
    /// <para>
    /// They stay in the catalog because the launcher is not their only reader — the taskbar,
    /// the host and the follow-up layer all resolve through it. This flags them so the one
    /// reader that offers a cold start can decline to.
    /// </para>
    /// </summary>
    public bool RequiresSubject { get; init; }

    /// <summary>How to reach a destination window, shown where it cannot be opened directly.</summary>
    public string? ReachedBy { get; init; }

    /// <summary>One line, shown in the launcher. Says what the window does, plainly.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether only the operator may see this window — a record of their own, or a form that
    /// writes one. A visitor's desk leaves it out everywhere (<see cref="LineOps.Desk.IDeskAccess"/>).
    /// </summary>
    public bool OperatorOnly { get; init; }
}

/// <summary>
/// What a window showed at one point: which view, aimed at what, under what title. Kept by the
/// window as it follows links, so its Back key can put the last page back.
/// </summary>
public sealed record WindowPage(
    WindowDefinition Definition,
    IReadOnlyDictionary<string, object> Parameters,
    string? TitleOverride)
{
    public string Title => TitleOverride ?? Definition.Title;
}

/// <summary>One open window. Mutable — the manager owns it and raises change notifications.</summary>
public class WindowInstance
{
    public required string Id { get; init; }

    /// <summary>
    /// The view the window is showing. Settable because a subject window follows its own links:
    /// a team named in a game opens in the game's window, which then shows the team.
    /// </summary>
    public required WindowDefinition Definition { get; set; }

    /// <summary>
    /// Whether a link followed inside this window lands in it rather than in a window of its own.
    ///
    /// <para>
    /// Only subject windows do — a game, a team, a player, a head to head. They are about one
    /// thing, so going from that thing to the next is reading on. A catalogue window is a place
    /// the operator works from: the Board turned into a game would lose the slate the game was
    /// picked from, so its links still open beside it.
    /// </para>
    /// </summary>
    public bool FollowsInPlace => Navigable(Definition);

    /// <summary>Whether a view can be a page in a window that follows its links.</summary>
    public static bool Navigable(WindowDefinition definition)
        => definition.RequiresSubject && !definition.Singleton;

    /// <summary>The pages this window came through, oldest first. The manager owns both lists.</summary>
    internal List<WindowPage> BackPages { get; set; } = [];

    internal List<WindowPage> ForwardPages { get; set; } = [];

    public bool CanPageBack => BackPages.Count > 0;

    public bool CanPageForward => ForwardPages.Count > 0;

    /// <summary>What Back would show — said on the key, so the reader knows where it goes.</summary>
    public string? BackTitle => BackPages.Count > 0 ? BackPages[^1].Title : null;

    public string? ForwardTitle => ForwardPages.Count > 0 ? ForwardPages[^1].Title : null;

    /// <summary>
    /// Bumped whenever the window turns to another page. The host keys the hosted view on it, so
    /// a new page is a new component — scrolled to the top, with none of the last page's tabs or
    /// filters carried over.
    /// </summary>
    public int PageVersion { get; internal set; }

    /// <summary>Overrides the definition's title, e.g. a specific matchup.</summary>
    public string? TitleOverride { get; set; }

    public string Title => TitleOverride ?? Definition.Title;

    // Computed by the manager from Weight and row order; nothing else writes these.
    public double X { get; set; }
    public double Width { get; set; }

    /// <summary>Relative share of the row when expanded. Divider drags change this, nothing else.</summary>
    public double Weight { get; set; } = 1;

    /// <summary>
    /// Collapsed to a slim, title-only tab. A collapsed window is still in the row at its
    /// normal position — there is nowhere else for it to go, since the row is the only list
    /// of open windows there is.
    /// </summary>
    public bool Minimised { get; set; }

    /// <summary>Takes the whole row temporarily, without disturbing the other weights.</summary>
    public bool Maximised { get; set; }

    public PulseState Pulse { get; set; } = PulseState.Idle;

    /// <summary>Short status text on the title bar, e.g. "9m fresh" or "2 open".</summary>
    public string? StatusText { get; set; }

    /// <summary>
    /// Drives least-recently-used eviction. Set on open and on every focus, so the window
    /// closed to make room is always the one the operator has ignored longest.
    /// </summary>
    public DateTimeOffset LastFocusedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Position in the row, left to right. Mutable — dragging a window's tab past a neighbour
    /// reassigns these for every window at once (see <see cref="WindowManager.Reorder"/>).
    /// Focus alone never changes it: a row that reshuffles when clicked would be unusable.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>Arbitrary parameters passed to the hosted component, e.g. a game id.</summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
}

/// <summary>
/// A named set of windows to open, in row order. Fractions are not stored — the horizontal
/// layout decides widths, so a workspace saved anywhere works everywhere.
/// </summary>
public record Workspace(string Name, string Description, IReadOnlyList<string> WindowKeys);
