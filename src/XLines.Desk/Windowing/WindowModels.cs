namespace XLines.Desk.Windowing;

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
/// There is no layout setting: the desk is always a horizontal row of full-height columns, and
/// past the column limit windows share a column as tabs — there is no separate taskbar to configure.
/// Freedom to place things arbitrarily belongs to modals and popovers layered above the
/// desk, not to the desk itself.
/// </summary>
public class DeskSettings
{
    /// <summary>
    /// How many columns the row shows at once. Reaching it closes nothing: the next window joins
    /// a column as a tab (<see cref="WindowManager"/>, ADR 0027), so the limit is how many windows
    /// are in view side by side, not how many may be open. The name is the one a saved layout
    /// already carries.
    /// </summary>
    public int MaxConcurrentWindows { get; set; } = 4;

    /// <summary>
    /// How many windows the desk holds in all, columns and tabs together. Every window stays
    /// mounted — a tab keeps its panel, its scroll and its unsaved input — and each one costs the
    /// circuit its panel's memory and its refreshes, so there is an end; it is set far past what a
    /// person keeps in mind. At it, an open is refused and the header says why: nothing closes.
    /// </summary>
    public const int MaxOpenWindows = 24;

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

    // 90%, not 80%: under 90% the caption rung falls below 10px, the macOS floor, so the Window
    // manager no longer offers 80% and a stored 80% is lifted to the floor on restore.
    public const double MinUiScale = 0.9;
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
    /// An order, a customer and a product view are destinations: they take an id, and a window built
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
    /// writes one. A visitor's desk leaves it out everywhere (<see cref="XLines.Desk.IDeskAccess"/>).
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
    /// a customer named on an order opens in the order's window, which then shows the customer.
    /// </summary>
    public required WindowDefinition Definition { get; set; }

    /// <summary>
    /// Whether a link followed inside this window lands in it rather than in a window of its own.
    ///
    /// <para>
    /// Only subject windows do — an order, a customer, a product, a comparison. They are about one
    /// thing, so going from that thing to the next is reading on. A catalogue window is a place
    /// the operator works from: a list turned into an order would lose the list the order was
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

    /// <summary>
    /// Bumped by the manager whenever it changes anything this window draws — its status, title,
    /// page, width, place in the row, collapsed or maximised. The window's chrome compares it with
    /// the version it last drew and declines a render that would draw the same thing, which is
    /// what lets the desk re-render without redrawing every window on it.
    /// </summary>
    public int Version { get; private set; }

    internal void Touch() => Version++;

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
    /// The panel holds input that has not been saved — a half-typed entry. Its close key asks
    /// before discarding it, and so do Close all, a workspace and a step Back that would take it.
    /// Behind another tab it is kept, input and all. Set by the panel through
    /// <see cref="WindowManager.SetUnsaved"/>, nothing else.
    /// </summary>
    public bool HasUnsavedInput { get; internal set; }

    /// <summary>
    /// What would be lost, as the object of "Discard …?" — "this log entry". Null asks about
    /// "unsaved changes".
    /// </summary>
    public string? UnsavedInputLabel { get; internal set; }

    /// <summary>When the window was last opened, focused or shown. Read by nothing that decides; kept for the record.</summary>
    public DateTimeOffset LastFocusedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The column's place in the row, left to right; every tab in a column shares it. Mutable —
    /// dragging a column's bar past a neighbour reassigns these for every column at once (see
    /// <see cref="WindowManager.Reorder"/>). Focus alone never changes it: a row that reshuffles
    /// when clicked would be unusable.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The column this window is in. Windows that share one are a tab group: one shows, the rest
    /// wait behind it as tabs (<see cref="WindowColumn"/>). A window alone in its column is a
    /// plain window. The manager owns it.
    /// </summary>
    public string ColumnId { get; internal set; } = string.Empty;

    /// <summary>
    /// When the window was last shown, as a count the manager keeps: the highest in a column is
    /// the tab showing, and the rest follow in the order they were last shown. A count rather
    /// than a clock, because two windows opened in one tick must still have an order.
    /// </summary>
    internal long Recency { get; set; }

    /// <summary>Arbitrary parameters passed to the hosted component, e.g. an order id.</summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
}

/// <summary>
/// One column of the row: the windows in it, the one showing first and the rest in the order they
/// were last shown. A column of one is a plain window; of more, a tab group whose pop-up key is its
/// title bar. A snapshot — the manager builds a fresh one whenever it is asked.
/// </summary>
public sealed class WindowColumn
{
    internal WindowColumn(string id, IReadOnlyList<WindowInstance> tabs, int position)
    {
        Id = id;
        Tabs = tabs;
        Position = position;
    }

    public string Id { get; }

    /// <summary>Most recently shown first. Never empty.</summary>
    public IReadOnlyList<WindowInstance> Tabs { get; }

    /// <summary>The column's place in the row, from 0 at the left.</summary>
    public int Position { get; }

    /// <summary>The window in view; the one its tab key names.</summary>
    public WindowInstance Showing => Tabs[0];

    /// <summary>More than one window: drawn with a tab key in place of the title.</summary>
    public bool IsGroup => Tabs.Count > 1;

    /// <summary>The narrowest the column may be: its widest tab's minimum, so switching tabs never reflows the row.</summary>
    public int MinWidth => Tabs.Max(t => t.Definition.MinWidth);
}

/// <summary>
/// A named set of windows to open, in row order. Fractions are not stored — the horizontal
/// layout decides widths, so a workspace saved anywhere works everywhere.
/// </summary>
/// <param name="Name">What the brand menu lists it as.</param>
/// <param name="Description">One line under the name.</param>
/// <param name="WindowKeys">The catalogue keys to open, column by column, left to right.</param>
/// <param name="Columns">
/// Which column each key goes in, by index, when the workspace keeps tab groups; within a column
/// the first key listed is the one showing. Null opens one column per key, and any past the limit
/// fold into tabs left to right.
/// </param>
/// <param name="Icon">
/// A Material icon string drawn beside the name where the workspace is offered as a choice — the
/// empty desk, the brand menu. Null draws the generic workspace glyph.
/// </param>
/// <param name="Tag">
/// A marker the application defines and reads; the Desk carries it and never interprets it.
/// </param>
/// <remarks>
/// <see cref="Description"/> is the one sentence saying what the workspace is for. It is a
/// non-null string for the constructions that already pass one; an empty string reads as absent.
/// </remarks>
public record Workspace(
    string Name,
    string Description,
    IReadOnlyList<string> WindowKeys,
    IReadOnlyList<int>? Columns = null,
    string? Icon = null,
    string? Tag = null);

/// <summary>
/// A workspace as an empty desk offers it: the workspace, the titles of the windows it opens that
/// this person may see, in its order, and those titles as a sentence ("One, Two and Three").
/// </summary>
public sealed record OfferedWorkspace(Workspace Workspace, IReadOnlyList<string> Titles, string Windows);