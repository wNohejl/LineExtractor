namespace LineOps.Web.Windowing;

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
    public string? PrimaryWindowKey { get; set; } = WindowCatalog.Ops;

    /// <summary>Fraction of the row the primary window takes when more than one is open.</summary>
    public double PrimaryShare { get; set; } = 0.45;

    public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Auto;
    public int CustomWidth { get; set; } = 1920;
    public int CustomHeight { get; set; } = 1080;

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
/// A window that can be opened. Registered once in <see cref="WindowCatalog"/>; the
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
}

/// <summary>One open window. Mutable — the manager owns it and raises change notifications.</summary>
public class WindowInstance
{
    public required string Id { get; init; }
    public required WindowDefinition Definition { get; init; }

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
