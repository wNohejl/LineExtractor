namespace XLines.Desk.Windowing;

/// <summary>
/// Owns every open window: the columns of the row, the tabs in each, widths, and focus.
///
/// Scoped per circuit, so each browser session has its own desk. State lives here rather
/// than in the components so that every surface — header, windows, dividers — reads one
/// source of truth.
///
/// The desk is always a horizontal row of full-height columns, up to the operator's column
/// limit. Past it nothing closes: the next window joins a column as a tab, and that column
/// becomes a tab group showing one window at a time (ADR 0027). Reordering the row (dragging
/// a column's bar past a neighbour) and resizing it (dragging a divider) are the only two
/// gestures on the row, and they are deliberately distinct hit targets so neither is ever
/// triggered by accident.
/// </summary>
public class WindowManager
{
    /// <summary>Gap between columns and around the row, in CSS pixels.</summary>
    private const double Gap = 8;

    private readonly List<WindowInstance> _windows = [];
    private long _sequence;
    private long _recency;
    private int _columnSequence;

    /// <summary>
    /// True once the operator has set the ceiling themselves, after which the first viewport
    /// measurement stops overriding it.
    /// </summary>
    private bool _ceilingChosen;

    private bool _viewportMeasured;

    /// <summary>
    /// Raised whenever the desk as a whole has changed: which windows are open, their order,
    /// widths and focus, the settings, the notice, the saved workspaces. Not for a change that
    /// stays inside one window — that is <see cref="WindowChanged"/>.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised with a window's id when something only that window draws has changed — its pulse,
    /// its status line. Kept apart from <see cref="Changed"/> because panels report often, at
    /// least twice as each one loads, and one event for both re-rendered the desk, the header,
    /// the footer and every window on it to repaint one status line. The window it names, the
    /// window whose tab key and menu carry it, and the bar carrying its dot listen; nothing else needs to.
    /// </summary>
    public event Action<string>? WindowChanged;

    /// <summary>
    /// Raised when a close was asked for and a window holds unsaved input, with that window. The
    /// close did not happen; whoever asked puts the question, and calls <see cref="Close"/> or
    /// <see cref="CloseAll"/> if the answer is Discard.
    /// </summary>
    public event Action<WindowInstance>? CloseNeedsConfirmation;

    /// <summary>Raised when something asks for the command palette — the hotkey or the header key.</summary>
    public event Action? PaletteRequested;

    public void RequestPalette() => PaletteRequested?.Invoke();

    /// <summary>
    /// The windows this desk can open. Supplied by the application, read by everything here —
    /// through <see cref="IDeskAccess"/>, so a visitor's catalogue has no operator-only window
    /// in it and nothing that reads it has to remember to ask.
    /// </summary>
    public IWindowCatalog Catalog { get; }

    /// <summary>Who is at this desk. Read by the application where it draws a control that writes.</summary>
    public IDeskAccess Access { get; }

    public WindowManager(IWindowCatalog catalog)
        : this(catalog, OpenDeskAccess.Instance)
    {
    }

    public WindowManager(IWindowCatalog catalog, IDeskAccess access)
    {
        Access = access;
        Catalog = new AccessCatalog(catalog, access);
        Settings.PrimaryWindowKey = Catalog.DefaultPrimary;
    }

    /// <summary>Every open window, in the order they were opened — showing or waiting as a tab.</summary>
    public IReadOnlyList<WindowInstance> Windows => _windows;

    public DeskSettings Settings { get; } = new();

    /// <summary>The window the operator is in. Always one that is showing, never a tab behind another.</summary>
    public string? FocusedId { get; private set; }

    /// <summary>Measured browser desk area in CSS pixels. Set by the host on resize.</summary>
    public double ViewportWidth { get; private set; } = 1280;
    public double ViewportHeight { get; private set; } = 800;

    /// <summary>
    /// The size layout works against: the browser, or the resolution the operator declared.
    /// Declaring one lets you lay a desk out for the screen you will run it on rather than
    /// the one you are configuring it from.
    /// </summary>
    public double DeskWidth => Settings.ResolutionMode == ResolutionMode.Fixed
        ? Settings.CustomWidth
        : ViewportWidth;

    public double DeskHeight => Settings.ResolutionMode == ResolutionMode.Fixed
        ? Settings.CustomHeight
        : ViewportHeight;

    /// <summary>
    /// The narrowest a column may be before the window in it stops being readable. The widest
    /// <c>MinWidth</c> in the catalogue, so the guidance holds for whatever is opened rather
    /// than for the most forgiving thing that could be.
    /// </summary>
    private const double ReadableWidth = 420;

    /// <summary>
    /// How many columns fit at a readable width. Guidance beside the operator's ceiling.
    ///
    /// <para>
    /// This has to survive the layout it is guiding. It used to be <c>DeskWidth / 420</c>, a
    /// uniform division — but the row is not divided uniformly: the primary takes its share
    /// off the top and the rest split what is left. At 1920 with the default 45% primary that
    /// said four columns fit, and the three beside the primary landed at 345px each. Being
    /// under their own minimum they clamped back up, the row summed past the viewport, and the
    /// desk scrolled with the last window off-screen — while the number that produced the
    /// arrangement was also the default for the ceiling, so it recommended itself.
    /// </para>
    /// </summary>
    public int RecommendedCapacity
    {
        get
        {
            var hasPrimary = Settings.PrimaryWindowKey is not null;
            var share = Math.Clamp(Settings.PrimaryShare, 0.2, 0.8);

            for (var n = 12; n > 1; n--)
            {
                var usable = DeskWidth - Gap * (n + 1);

                if (usable <= 0)
                    continue;

                // Without a primary every column is the narrowest; with one, the narrowest is
                // whatever is left after its share is taken.
                var narrowest = hasPrimary
                    ? usable * (1 - share) / (n - 1)
                    : usable / n;

                if (narrowest >= ReadableWidth)
                    return n;
            }

            // One window is always allowed the whole desk, however narrow. The alternative is
            // guidance that recommends showing nothing.
            return 1;
        }
    }

    /// <summary>Every window open, showing or behind a tab.</summary>
    public int OpenCount => _windows.Count;

    /// <summary>Columns in the row — never more than the limit.</summary>
    public int ColumnCount => _windows.Select(w => w.ColumnId).Distinct().Count();

    /// <summary>Windows waiting behind another in a tab group.</summary>
    public int TabbedCount => OpenCount - ColumnCount;

    /// <summary>
    /// Every column is in use: the next window opens as a tab (<see cref="NextTabTarget"/>), and
    /// a follow-up floats rather than covering a column (<see cref="FollowUpLauncher"/>).
    /// </summary>
    public bool AtCapacity => ColumnCount >= Settings.MaxConcurrentWindows;

    /// <summary>The desk holds <see cref="DeskSettings.MaxOpenWindows"/>: the next open is refused, and says so.</summary>
    public bool AtCeiling => OpenCount >= DeskSettings.MaxOpenWindows;

    /// <summary>
    /// A sentence for the header: a window closed by its own key, with the way back, or an open
    /// the desk refused, with why. Nothing on the desk vanishes or fails to appear unexplained.
    /// </summary>
    public string? Notice { get; private set; }

    /// <summary>
    /// The titles behind <see cref="Notice"/>, so the key that offers to bring them back can name
    /// them without reading the sentence apart. Empty when the notice is a refusal.
    /// </summary>
    public IReadOnlyList<string> NoticeTitles { get; private set; } = [];

    /// <summary>
    /// The workspace last put on the desk with <see cref="ApplyWorkspace"/>, the application's
    /// or one the operator saved. Cleared when the desk is emptied or another is applied; kept
    /// while windows are opened, closed and moved within it, since the arrangement is still the
    /// one it began as. Written into the layout by name and found again on restore.
    /// </summary>
    public Workspace? ActiveWorkspace { get; private set; }

    /// <summary>
    /// Clears the notice only if it is still <paramref name="expected"/> — the very sentence, not
    /// the same words — so whoever was showing an older one cannot clear a newer one by being late.
    /// True when it cleared.
    /// </summary>
    public bool ClearNotice(string expected)
    {
        if (!ReferenceEquals(Notice, expected))
            return false;

        ClearNotice();
        return true;
    }

    public void ClearNotice()
    {
        if (Notice is null)
            return;

        Notice = null;
        NoticeTitles = [];
        Notify();
    }

    public void UpdateSettings(Action<DeskSettings> mutate)
    {
        var before = Settings.MaxConcurrentWindows;
        mutate(Settings);

        if (Settings.MaxConcurrentWindows != before)
            _ceilingChosen = true;

        // The limit may have dropped below the columns in the row: the extra ones fold into tabs.
        // Nothing closes, so nothing typed is at risk.
        FoldToLimit();
        Relayout();
    }

    public void SetViewport(double width, double height)
    {
        var changed = Math.Abs(width - ViewportWidth) >= 1 || Math.Abs(height - ViewportHeight) >= 1;

        ViewportWidth = width;
        ViewportHeight = height;

        // First real measurement sets the default ceiling from the screen. Shipping a fixed
        // default means the row overflows out of the box on a laptop and every column sits
        // pinned at its minimum, which makes the dividers inert — the layout would be
        // technically correct and useless. The operator's own choice always wins after this.
        if (!_viewportMeasured && width > 0)
        {
            _viewportMeasured = true;

            if (!_ceilingChosen)
            {
                Settings.MaxConcurrentWindows = Math.Max(2, RecommendedCapacity);
                FoldToLimit();
            }

            Relayout();
            return;
        }

        if (changed)
            Relayout();
    }

    // ------------------------------------------------------------------ columns ----

    /// <summary>The row, left to right, each column with its tabs most recently shown first.</summary>
    public IReadOnlyList<WindowColumn> Columns => BuildColumns();

    private List<WindowColumn> BuildColumns()
    {
        var columns = _windows
            .GroupBy(w => w.ColumnId)
            .Select(g => (Id: g.Key,
                          Tabs: g.OrderByDescending(w => w.Recency).ThenBy(w => w.Sequence).ToList(),
                          Order: g.Min(w => w.Sequence)))
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Tabs.Min(w => _windows.IndexOf(w)))
            .ToList();

        return columns.Select((c, i) => new WindowColumn(c.Id, c.Tabs, i)).ToList();
    }

    /// <summary>The column a window is in, or null for a window not on the desk.</summary>
    public WindowColumn? ColumnOf(string id)
        => Find(id) is { } window ? BuildColumns().First(c => c.Id == window.ColumnId) : null;

    /// <summary>Whether a window is the one its column shows, rather than a tab behind it.</summary>
    public bool IsShowing(WindowInstance window) => ColumnOf(window.Id)?.Showing == window;

    /// <summary>The narrowest the column holding <paramref name="window"/> may be. See <see cref="WindowColumn.MinWidth"/>.</summary>
    public int MinWidthOf(WindowInstance window) => ColumnOf(window.Id)?.MinWidth ?? window.Definition.MinWidth;

    /// <summary>
    /// The column the next window would join as a tab, or null while a column is free. Said
    /// before the click, as the header's count does, so where a window lands is never a surprise.
    /// </summary>
    public WindowColumn? NextTabTarget => AtCapacity ? TabTarget(BuildColumns()) : null;

    /// <summary>
    /// Where a window opened at the limit goes: a tab in the window last selected, so what was
    /// just asked for lands where the operator is looking (amended 2026-11-23). It used to avoid
    /// that column and spread tabs across the others, and a window chosen from the menu turned
    /// up somewhere other than the window the operator had been in. With nothing selected the
    /// old rule (<see cref="Pick"/>) still chooses.
    /// </summary>
    private WindowColumn? TabTarget(IReadOnlyList<WindowColumn> columns)
        => (FocusedId is { } focused ? columns.FirstOrDefault(c => c.Tabs.Any(t => t.Id == focused)) : null)
           ?? Pick(columns, avoid: null);

    /// <summary>
    /// Where a column folds when the limit drops, and where a window opened at the limit goes
    /// with nothing selected. One rule, read left to right:
    ///
    /// <list type="number">
    /// <item>never the column the operator is working in, while there is another — the window
    /// they are reading stays in view beside the new one;</item>
    /// <item>a collapsed column only when every column is — what was set aside stays aside;</item>
    /// <item>the primary window's column last, so the larger share keeps showing the window it
    /// was given to;</item>
    /// <item>then the column with the fewest tabs, the leftmost of those first.</item>
    /// </list>
    ///
    /// The last two make the groups fill left to right and stay balanced: with three columns,
    /// the fourth window tabs into the first, the fifth into the second, the sixth into the third.
    /// </summary>
    private WindowColumn? Pick(IReadOnlyList<WindowColumn> columns, string? avoid)
    {
        if (columns.Count == 0)
            return null;

        var candidates = columns.Where(c => c.Tabs.All(t => t.Id != avoid)).ToList();

        if (candidates.Count == 0)
            candidates = [.. columns];

        return candidates
            .OrderBy(c => c.Showing.Minimised)
            .ThenBy(IsPrimaryColumn)
            .ThenBy(c => c.Tabs.Count)
            .ThenBy(c => c.Position)
            .First();
    }

    private bool IsPrimaryColumn(WindowColumn column)
        => Settings.PrimaryWindowKey is { } key && column.Tabs.Any(t => t.Definition.Key == key);

    private string NewColumnId() => $"col-{++_columnSequence}";

    /// <summary>
    /// Puts <paramref name="window"/> in <paramref name="column"/>, sharing its place and width.
    /// In front of the tabs already there when it is the one being opened; behind them when a
    /// column is folding in, so what the column was showing keeps showing.
    /// </summary>
    private void Join(WindowInstance window, WindowColumn column, bool inFront)
    {
        window.ColumnId = column.Id;
        window.Sequence = column.Tabs.Min(t => t.Sequence);
        window.Weight = column.Showing.Weight;

        // Maximised is the column's: a tab joining one that holds the row holds it with the rest,
        // or the bar of whichever came forward would offer the wrong key.
        window.Maximised = column.Showing.Maximised;

        if (inFront)
        {
            // The window just opened must be seen, so a collapsed column it lands in opens out.
            foreach (var tab in column.Tabs)
                tab.Minimised = false;

            window.Minimised = false;
            window.Recency = ++_recency;
        }
        else
        {
            window.Minimised = column.Showing.Minimised;
            window.Recency = column.Tabs.Min(t => t.Recency) - 1;
        }

        window.Touch();
        TouchAll(column.Tabs);
    }

    /// <summary>
    /// Folds the columns past the limit into tabs, the leftmost extra column first, each into the
    /// column <see cref="Pick"/> chooses among those that remain. The tabs that move go behind the
    /// ones there, in the order they had, so every column the operator can see keeps showing what
    /// it showed. Closes nothing.
    /// </summary>
    private void FoldToLimit()
    {
        var limit = Math.Max(1, Settings.MaxConcurrentWindows);

        while (true)
        {
            var columns = BuildColumns();

            if (columns.Count <= limit)
                return;

            var extra = columns[limit];
            var target = Pick(columns.Take(limit).ToList(), avoid: null)!;

            // Most recent first, each landing behind the one moved before it, so the order holds.
            foreach (var tab in extra.Tabs)
            {
                Join(tab, target, inFront: false);
                target = BuildColumns().First(c => c.Id == target.Id);
            }

            if (FocusedId is { } focused && extra.Tabs.Any(t => t.Id == focused))
                FocusedId = target.Showing.Id;
        }
    }

    /// <summary>Whether a free column is waiting for a tab to move into it.</summary>
    public bool CanMoveToOwnColumn(string id)
        => ColumnOf(id) is { IsGroup: true } && !AtCapacity;

    /// <summary>
    /// Takes a window out of its tab group into a column of its own, just right of the one it
    /// left, at its own default width. Only while a column is free: the limit is the operator's,
    /// and this does not raise it. True when it moved.
    /// </summary>
    public bool MoveToOwnColumn(string id)
    {
        if (!CanMoveToOwnColumn(id) || Find(id) is not { } window)
            return false;

        // The row changes shape, so it is a step Back takes back: the tab returns to its group,
        // at the group's width.
        Remember();

        var columns = BuildColumns();
        var from = columns.First(c => c.Id == window.ColumnId);

        window.ColumnId = NewColumnId();
        window.Weight = window.Definition.DefaultWeight;
        window.Minimised = false;
        window.Maximised = false;

        var order = columns.Select(c => c.Tabs.Where(t => t != window).ToList()).ToList();
        order.Insert(from.Position + 1, [window]);
        Renumber(order);

        window.Recency = ++_recency;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        FocusedId = id;

        // The column it left may hold the row; the window the operator is now in must be drawn.
        GiveRowBackFor(window);

        TouchAll(from.Tabs);
        Relayout();
        return true;
    }

    /// <summary>
    /// Whether <paramref name="id"/> can be dropped into the column holding <paramref name="targetId"/>
    /// as a tab: both on the desk, in different columns.
    /// </summary>
    public bool CanMoveIntoColumn(string id, string targetId)
        => Find(id) is { } window && Find(targetId) is { } target && window.ColumnId != target.ColumnId;

    /// <summary>
    /// A tab dragged onto another window: <paramref name="id"/> leaves its column and joins the
    /// one holding <paramref name="targetId"/>, in front, at that column's place and width. A
    /// column left empty gives its width back to the row; one left with a single window is a
    /// plain window again. A step Back takes back. True when it moved.
    /// </summary>
    public bool MoveIntoColumn(string id, string targetId)
    {
        if (!CanMoveIntoColumn(id, targetId))
            return false;

        Remember();

        var window = Find(id)!;
        var columns = BuildColumns();
        var from = columns.First(c => c.Id == window.ColumnId);
        var to = columns.First(c => c.Id == Find(targetId)!.ColumnId);

        Join(window, to, inFront: true);
        TouchAll(from.Tabs);

        // The row closes up behind a column the move emptied, and keeps every other place.
        Renumber(BuildColumns().Select(c => c.Tabs));

        window.LastFocusedAt = DateTimeOffset.UtcNow;
        FocusedId = id;
        GiveRowBackFor(window);

        Relayout();
        return true;
    }

    /// <summary>Gives every column its place in the row, from 0, and moves the version of each window whose place moved.</summary>
    private static void Renumber(IEnumerable<IEnumerable<WindowInstance>> columns)
    {
        var position = 0;

        foreach (var column in columns)
        {
            foreach (var window in column.Where(w => w.Sequence != position))
            {
                window.Sequence = position;
                window.Touch();
            }

            position++;
        }
    }

    private static void TouchAll(IEnumerable<WindowInstance> windows)
    {
        foreach (var window in windows)
            window.Touch();
    }

    /// <summary>
    /// Restores the row when a column other than <paramref name="window"/>'s holds it. Focus is
    /// always on a window that is drawn, and while a column is maximised only that column is in
    /// view: a window reached outside it — from the palette, a follow-up, a tab moved out — would
    /// otherwise be focused behind it, unseen.
    /// </summary>
    private void GiveRowBackFor(WindowInstance window)
    {
        if (!_windows.Any(w => w.Maximised && w.ColumnId != window.ColumnId))
            return;

        foreach (var w in _windows.Where(w => w.Maximised))
        {
            w.Maximised = false;
            w.Touch();
        }
    }

    /// <summary>
    /// Asks the tab key of the column that will show <paramref name="id"/> to take keyboard focus once
    /// drawn. Choosing a tab from the keyboard brings another window forward, and the key that
    /// held focus goes behind it with it; without this the keyboard would be left on the page.
    /// </summary>
    public void RequestTabFocus(string id) => _tabFocus = id;

    /// <summary>True, once, for the tab key drawn for the window focus was asked for.</summary>
    public bool TakeTabFocus(string id)
    {
        if (_tabFocus != id)
            return false;

        _tabFocus = null;
        return true;
    }

    private string? _tabFocus;

    // -------------------------------------------------------------------- open ----

    /// <summary>
    /// Opens a window: a column of its own while one is free, and a tab in an existing column
    /// once every column is in use (<see cref="Pick"/>). Nothing closes to make room.
    ///
    /// <para>
    /// The one refusal is the desk's ceiling, <see cref="DeskSettings.MaxOpenWindows"/>: then
    /// nothing opens, <see cref="Notice"/> says why, and what comes back is the window the
    /// operator is in.
    /// </para>
    /// </summary>
    public WindowInstance Open(
        WindowDefinition definition,
        Dictionary<string, object>? parameters = null,
        string? titleOverride = null)
    {
        // Every way a visitor could reach an operator window goes through Catalog, which does
        // not have one; arriving here with one means a control was drawn that should not have been.
        if (definition.OperatorOnly && !Access.CanOperate)
            throw new InvalidOperationException(
                $"'{definition.Key}' is an operator window and this desk is a visitor's.");

        if (definition.Singleton)
        {
            var existing = _windows.FirstOrDefault(w => w.Definition.Key == definition.Key);
            if (existing is not null)
            {
                // Asked for on a subject — runs for one source, one incident — the window already
                // on the desk is re-aimed at it. Focusing it as it stood showed whatever it was
                // last opened on, which read as the desk ignoring the click. Re-aiming replaces
                // what it showed, so it is a step Back can undo.
                if (Retargets(existing, parameters, titleOverride))
                {
                    Remember();
                    Aim(existing.Parameters, parameters ?? new Dictionary<string, object>());

                    if (titleOverride is not null)
                        existing.TitleOverride = titleOverride;
                }

                // A tab behind another is brought forward: the palette, the tab menu and a follow-up
                // never open a second copy of a window already on the desk.
                Focus(existing.Id);
                return existing;
            }
        }

        else if (_windows.FirstOrDefault(w => w.Definition.Key == definition.Key && SameSubject(w, parameters)) is { } same)
        {
            // A window that may be open more than once is open once per subject: asking again
            // for a subject already on the desk brings that window forward,
            // wherever it is — a tab behind another included — rather than drawing a second copy.
            Focus(same.Id);
            return same;
        }

        var title = titleOverride ?? definition.Title;

        if (AtCeiling)
        {
            Notice = $"The desk holds {DeskSettings.MaxOpenWindows} windows, its most. Close one to open {title}.";
            NoticeTitles = [];

            var here = (FocusedId is { } focused ? Find(focused) : null) ?? MostRecent() ?? _windows[0];
            Notify();
            return here;
        }

        Remember();

        var instance = new WindowInstance
        {
            Id = $"{definition.Key}-{++_sequence}",
            Sequence = _sequence,
            Definition = definition,
            TitleOverride = titleOverride,
            Weight = definition.DefaultWeight,
            Recency = ++_recency,
            LastFocusedAt = DateTimeOffset.UtcNow
        };

        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
                instance.Parameters[key] = value;
        }

        if (NextTabTarget is { } target)
            Join(instance, target, inFront: true);
        else
            instance.ColumnId = NewColumnId();

        _windows.Add(instance);
        FocusedId = instance.Id;
        GiveRowBackFor(instance);
        Notice = null;
        NoticeTitles = [];

        // A new column takes its place at the end of the row and the others give up the space
        // for it; a new tab takes its column's place and width, and the row does not move.
        Relayout();

        return instance;
    }

    /// <summary>
    /// Closes a window unless it holds unsaved input, in which case nothing closes and
    /// <see cref="CloseNeedsConfirmation"/> is raised with it. True when the window is gone.
    /// The close key goes through here; <see cref="Close"/> is what the answer Discard calls.
    /// </summary>
    public bool TryClose(string id)
    {
        if (Find(id) is { HasUnsavedInput: true } window)
        {
            CloseNeedsConfirmation?.Invoke(window);
            return false;
        }

        Close(id);
        return true;
    }

    /// <summary>
    /// Closes every window unless one holds unsaved input. One question for the lot, about the
    /// first such window in the row, rather than one per window. True when the desk is empty.
    /// </summary>
    public bool TryCloseAll()
    {
        if (Row().FirstOrDefault(w => w.HasUnsavedInput) is { } window)
        {
            CloseNeedsConfirmation?.Invoke(window);
            return false;
        }

        CloseAll();
        return true;
    }

    /// <summary>
    /// Closes a window whatever it holds. See <see cref="TryClose"/>. Closing the tab a group is
    /// showing brings the next most recent forward; a group left with one window is a plain
    /// window again; a column left with none gives its width back to the row.
    /// </summary>
    public void Close(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        Remember();

        var column = ColumnOf(id)!;
        var rest = column.Tabs.Where(t => t != window).ToList();

        _windows.Remove(window);
        TouchAll(rest);

        // Said as a sentence with a key that names the window, so the way back is a key rather
        // than a header arrow whose direction had to be guessed.
        Notice = $"Closed {window.Title}.";
        NoticeTitles = [window.Title];

        // Focus stays in the column the operator was working in: the tab revealed there is
        // where they are now looking.
        if (FocusedId == id)
            FocusedId = rest.Count > 0 ? rest[0].Id : MostRecent()?.Id;

        Relayout();
    }

    /// <summary>Closes every window whatever they hold. See <see cref="TryCloseAll"/>.</summary>
    public void CloseAll()
    {
        if (_windows.Count > 0)
            Remember();

        _windows.Clear();
        FocusedId = null;
        Notice = null;
        NoticeTitles = [];
        ActiveWorkspace = null;
        Notify();
    }

    /// <summary>
    /// Makes a window the one the operator is in. A tab behind another comes to the front of its
    /// column — the one that was showing becomes the second tab — and a collapsed column opens out.
    /// </summary>
    public void Focus(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        var column = ColumnOf(id)!;
        var index = IndexIn(column, window);
        var wasCollapsed = column.Showing.Minimised;
        var heldElsewhere = _windows.Any(w => w.Maximised && w.ColumnId != window.ColumnId);

        foreach (var tab in column.Tabs)
            tab.Minimised = false;

        GiveRowBackFor(window);

        window.Recency = ++_recency;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        window.Touch();

        // Every tab in the column moves when one comes forward, so every window in the column
        // draws again: the one revealed, the one it covers, and the order between them.
        if (index > 0 || wasCollapsed)
            TouchAll(column.Tabs);

        FocusedId = id;

        // Expanding changes how width is shared, so it must be recomputed, and so does giving
        // the row back. A plain focus does not — the row keeps its order and sizes, which is
        // what makes a tiled desk stable to work in.
        if (wasCollapsed || heldElsewhere)
            Relayout();
        else
            Notify();
    }

    private static int IndexIn(WindowColumn column, WindowInstance window)
    {
        for (var i = 0; i < column.Tabs.Count; i++)
        {
            if (column.Tabs[i] == window)
                return i;
        }

        return 0;
    }

    /// <summary>Collapses a window's column to a title-only stub, or restores one that already is.</summary>
    public void ToggleMinimise(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        var column = ColumnOf(id)!;

        if (column.Showing.Minimised)
        {
            Focus(id);
            return;
        }

        // A column collapses whole: its tabs wait behind the stub as they waited behind the window,
        // panels and input kept (AppWindow hides the body rather than dropping it). A collapsed
        // column holds no row, so one that held it gives it back rather than taking it again the
        // moment it opens out.
        foreach (var tab in column.Tabs)
        {
            tab.Minimised = true;
            tab.Maximised = false;
            tab.Touch();
        }

        if (column.Tabs.Any(t => t.Id == FocusedId))
            FocusedId = MostRecent()?.Id;

        Relayout();
    }

    public void ToggleMaximise(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        // Maximised is the column's, held by every tab in it alike: the tab chosen after
        // maximising another reads the same state on its bar, and one press restores the row.
        var maximise = MaximisedColumn?.Id != window.ColumnId;

        // Only one column can hold the row at a time.
        foreach (var w in _windows.Where(w => w.Maximised))
        {
            w.Maximised = false;
            w.Touch();
        }

        // A collapsed window has nothing to maximise into, and a tab behind another is not in
        // view to maximise: both are brought forward first, as pressing them would.
        Focus(id);

        foreach (var tab in ColumnOf(id)!.Tabs)
        {
            tab.Maximised = maximise;
            tab.Touch();
        }

        Relayout();
    }

    /// <summary>The column holding the row on its own, if one is maximised.</summary>
    public WindowColumn? MaximisedColumn
        => BuildColumns().FirstOrDefault(c => c.Showing.Maximised && !c.Showing.Minimised);

    /// <summary>
    /// Reassigns the row's order from a dragged column's drop position. Any window of a column
    /// names the column; every column not mentioned keeps its relative order after those that
    /// are. Called with the complete row, so that case does not arise in practice.
    /// </summary>
    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        var columns = BuildColumns();
        var order = new List<WindowColumn>();

        foreach (var id in orderedIds)
        {
            if (Find(id) is { } window
                && columns.First(c => c.Id == window.ColumnId) is var column
                && !order.Contains(column))
            {
                order.Add(column);
            }
        }

        order.AddRange(columns.Where(c => !order.Contains(c)));
        Renumber(order.Select(c => c.Tabs));

        Relayout();
    }

    /// <summary>
    /// Applies a divider drag: the two adjacent columns swap width between them and nothing
    /// else in the row moves. Their combined weight is preserved, so the rest of the row is
    /// untouched — which is what makes a divider feel local rather than global. Every tab in a
    /// column shares its width, so a tab switch never resizes the row.
    /// </summary>
    public void AdjustSplit(string leftId, string rightId, double leftWidthPx, double rightWidthPx)
    {
        var left = ColumnOf(leftId);
        var right = ColumnOf(rightId);

        if (left is null || right is null || left.Id == right.Id)
            return;

        var totalWeight = left.Showing.Weight + right.Showing.Weight;
        var totalPx = leftWidthPx + rightWidthPx;

        if (totalPx <= 0 || totalWeight <= 0)
            return;

        var leftWeight = totalWeight * (leftWidthPx / totalPx);
        SetWeight(left, leftWeight);
        SetWeight(right, totalWeight - leftWeight);

        Relayout();
    }

    private static void SetWeight(WindowColumn column, double weight)
    {
        foreach (var tab in column.Tabs)
        {
            tab.Weight = weight;
            tab.Touch();
        }
    }

    /// <summary>A column's own width when nothing has been dragged: the widest default among its tabs.</summary>
    private static double DefaultWeightOf(WindowColumn column) => column.Tabs.Max(t => t.Definition.DefaultWeight);

    /// <summary>
    /// Whether a column still sits at a default width: its weight is the default one of its own
    /// tabs gives it — the one it opened at, which every tab that joined it took on.
    /// </summary>
    private static bool AtDefaultWidth(WindowColumn column)
        => column.Tabs.Any(t => Math.Abs(column.Showing.Weight - t.Definition.DefaultWeight) <= 0.001);

    /// <summary>Returns every column to the width the primary-window setting implies.</summary>
    public void ResetWidths()
    {
        foreach (var column in BuildColumns())
        {
            var weight = DefaultWeightOf(column);

            if (column.Tabs.Any(t => t.Weight != weight))
                SetWeight(column, weight);
        }

        Relayout();
    }

    /// <summary>
    /// Lets a hosted panel say it holds input that has not been saved, and what to call it in
    /// the question — "this log entry". Cleared when the input is saved or emptied.
    /// </summary>
    public void SetUnsaved(string id, bool dirty, string? label = null)
    {
        var window = Find(id);
        if (window is null)
            return;

        label = dirty ? label : null;

        if (window.HasUnsavedInput == dirty && window.UnsavedInputLabel == label)
            return;

        window.HasUnsavedInput = dirty;
        window.UnsavedInputLabel = label;

        // Nothing on the desk's chrome draws the flag — the close key reads it when it acts — so
        // this stays with the one window rather than redrawing the desk.
        NotifyWindow(window);
    }

    /// <summary>
    /// Lets a hosted panel report its own state up to its window chrome. Said to that window, the
    /// tab key of its column, and the bar alone (<see cref="WindowChanged"/>): a status line moves
    /// nothing else on the desk. A tab behind another still reports, and its tab says so.
    /// </summary>
    public void SetPulse(string id, PulseState pulse, string? statusText = null)
    {
        var window = Find(id);
        if (window is null)
            return;

        if (window.Pulse == pulse && window.StatusText == statusText)
            return;

        window.Pulse = pulse;
        window.StatusText = statusText;
        NotifyWindow(window);
    }

    /// <summary>
    /// Lays the columns out in one horizontal row, left to right in <see cref="WindowInstance.Sequence"/>
    /// order. Collapsed columns take a fixed slim width; the rest split what remains by weight.
    /// Every tab in a column takes the column's place and width, so the one that comes forward
    /// is already where it will be seen. The only layout there is.
    /// </summary>
    public void Relayout()
    {
        var columns = BuildColumns();

        if (columns.Count == 0)
        {
            // An empty desk is not any workspace, however it got there.
            ActiveWorkspace = null;
            Notify();
            return;
        }

        if (columns.Any(c => c.Showing.Maximised && !c.Showing.Minimised))
        {
            // Only the maximised column is in view — the others stay mounted, hidden behind it —
            // so their geometry is irrelevant; keeping it sane avoids a jump when it is restored.
            foreach (var w in _windows)
                Place(w, Gap, DeskWidth - Gap * 2);

            Notify();
            return;
        }

        var expanded = columns.Where(c => !c.Showing.Minimised).ToList();
        var collapsedCount = columns.Count - expanded.Count;

        ApplyPrimaryShare(expanded);

        var usable = DeskWidth - Gap * (columns.Count + 1) - collapsedCount * DeskSettings.CollapsedWidth;
        var totalWeight = expanded.Sum(c => c.Showing.Weight);

        if (totalWeight <= 0)
            totalWeight = Math.Max(expanded.Count, 1);

        var widths = ColumnWidths(expanded, Math.Max(usable, 0), totalWeight);
        var offset = Gap;

        foreach (var column in columns)
        {
            var width = column.Showing.Minimised ? DeskSettings.CollapsedWidth : widths[column.Id];

            foreach (var tab in column.Tabs)
                Place(tab, offset, width);

            offset += width + Gap;
        }

        Notify();
    }

    /// <summary>
    /// Sets a column's place in the row, and moves its version only when the place moved: a
    /// relayout that leaves a column where it was leaves its chrome undrawn, and one that moves
    /// it is drawn where it now is.
    /// </summary>
    private static void Place(WindowInstance window, double x, double width)
    {
        if (window.X == x && window.Width == width)
            return;

        window.X = x;
        window.Width = width;
        window.Touch();
    }

    /// <summary>
    /// Splits the row's usable width between the expanded columns by weight, and the row
    /// always sums to that width — it never runs past the desk.
    ///
    /// <para>
    /// A column whose share falls under its minimum is lifted to the minimum and the rest
    /// divide what is left, so a minimum costs its neighbours rather than the desk's edge.
    /// When the minimums together do not fit — two columns at 800px, one of them the list's
    /// 680 — they cannot all be honoured, and every column takes its plain share instead and
    /// scrolls inside itself. Overflowing the row instead put the last window's right half
    /// off-screen behind a sideways scroll, which is the arrangement capacity exists to avoid.
    /// </para>
    /// </summary>
    private static Dictionary<string, double> ColumnWidths(
        List<WindowColumn> expanded, double usable, double totalWeight)
    {
        var widths = new Dictionary<string, double>();

        if (expanded.Count == 0)
            return widths;

        double Share(WindowColumn c, double room, double weight)
            => weight > 0 ? room * (c.Showing.Weight / weight) : room / expanded.Count;

        if (expanded.Sum(c => c.MinWidth) > usable)
        {
            foreach (var c in expanded)
                widths[c.Id] = Share(c, usable, totalWeight);

            return widths;
        }

        var free = new List<WindowColumn>(expanded);
        var room = usable;

        // Lift every column under its minimum, then re-divide among the rest; lifting one can
        // push another under, so repeat until nothing more is lifted. Terminates: each pass
        // removes at least one column or stops.
        while (true)
        {
            var weight = free.Sum(c => c.Showing.Weight);
            var under = free.Where(c => Share(c, room, weight) < c.MinWidth).ToList();

            if (under.Count == 0)
            {
                foreach (var c in free)
                    widths[c.Id] = Share(c, room, weight);

                return widths;
            }

            foreach (var c in under)
            {
                widths[c.Id] = c.MinWidth;
                room -= c.MinWidth;
                free.Remove(c);
            }
        }
    }

    /// <summary>
    /// Every window, column by column left to right, the showing tab first in each. Includes
    /// collapsed columns and tabs behind — the row is the only list of open windows there is.
    /// </summary>
    private List<WindowInstance> Row() => BuildColumns().SelectMany(c => c.Tabs).ToList();

    /// <summary>
    /// Nudges the primary window's column so it lands on its configured share, unless the
    /// operator has since dragged a divider — a manual adjustment outranks a default. Only
    /// affects width: the column's position in the row is whatever the operator put it at, same
    /// as any other. The primary's column is the one holding the primary window, showing or not.
    /// </summary>
    private void ApplyPrimaryShare(List<WindowColumn> expanded)
    {
        if (Settings.PrimaryWindowKey is null || expanded.Count < 2)
            return;

        var primary = expanded.FirstOrDefault(IsPrimaryColumn);
        if (primary is null)
            return;

        // Untouched columns all sit at a default weight; if any differ, widths are the
        // operator's and this leaves them alone.
        var others = expanded.Where(c => c != primary).ToList();
        if (others.Any(c => !AtDefaultWidth(c)))
            return;

        var share = Math.Clamp(Settings.PrimaryShare, 0.2, 0.8);
        var othersWeight = others.Sum(c => c.Showing.Weight);

        var weight = othersWeight * share / (1 - share);

        if (primary.Tabs.All(t => t.Weight == weight))
            return;

        SetWeight(primary, weight);
    }

    /// <summary>
    /// Opens a named set of windows, replacing whatever is on the desk — unless a window on it
    /// holds unsaved input, in which case nothing changes and <see cref="CloseNeedsConfirmation"/>
    /// is raised with the first such window. A workspace replaces every window, so one typed-in
    /// window stops the lot, as Close all does. True when the workspace is on the desk.
    ///
    /// <para>
    /// The column limit stands. A workspace with more windows than columns opens the rest as
    /// tabs — in the columns it names, when it names them, or folded left to right when it does
    /// not — so the first windows it lists are the ones in view.
    /// </para>
    /// </summary>
    public bool ApplyWorkspace(Workspace workspace)
    {
        if (Row().FirstOrDefault(w => w.HasUnsavedInput) is { } dirty)
        {
            Refuse(dirty);
            return false;
        }

        if (_windows.Count > 0)
            Remember();

        _windows.Clear();
        FocusedId = null;
        Notice = null;
        NoticeTitles = [];

        var columns = workspace.Columns is { } named && named.Count == workspace.WindowKeys.Count ? named : null;
        var placed = new List<(WindowInstance Window, int Column, int Rank)>();

        for (var i = 0; i < workspace.WindowKeys.Count && placed.Count < DeskSettings.MaxOpenWindows; i++)
        {
            var definition = Catalog.Find(workspace.WindowKeys[i]);
            if (definition is null)
                continue;

            var column = columns?[i] ?? i;
            var instance = new WindowInstance
            {
                Id = $"{definition.Key}-{++_sequence}",
                Sequence = column,
                Definition = definition,
                Weight = definition.DefaultWeight,
                LastFocusedAt = DateTimeOffset.UtcNow
            };

            placed.Add((instance, column, placed.Count(p => p.Column == column)));
        }

        Arrange(placed);

        FocusedId = BuildColumns().FirstOrDefault()?.Showing.Id;
        ActiveWorkspace = workspace;
        FoldToLimit();
        Relayout();
        return true;
    }

    /// <summary>
    /// Puts windows built from a saved list on the desk: one column per distinct index, in index
    /// order, and inside each the window ranked 0 showing with the rest behind it in rank order.
    /// The tabs in one column take the weight of the one showing.
    /// </summary>
    private void Arrange(IEnumerable<(WindowInstance Window, int Column, int Rank)> placed)
    {
        foreach (var group in placed.GroupBy(p => p.Column).OrderBy(g => g.Key))
        {
            var id = NewColumnId();
            var tabs = group.OrderBy(p => p.Rank).Select(p => p.Window).ToList();

            // Showing last, so it takes the highest count.
            foreach (var window in Enumerable.Reverse(tabs))
            {
                window.ColumnId = id;
                window.Sequence = group.Key;
                window.Recency = ++_recency;
                _windows.Add(window);
            }

            foreach (var window in tabs.Skip(1))
                window.Weight = tabs[0].Weight;
        }

        Renumber(BuildColumns().Select(c => c.Tabs));
    }

    public WindowInstance? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    private readonly List<Workspace> _userWorkspaces = [];

    /// <summary>Workspaces the operator saved on this machine, after the application's own.</summary>
    public IReadOnlyList<Workspace> UserWorkspaces => _userWorkspaces;

    /// <summary>
    /// Saves the row as a named workspace, replacing one of the same name: its columns left to
    /// right and the tabs in each, the showing one first. Only windows that open on nothing go
    /// in: a workspace is a starting arrangement, and "this order's history" is not something to
    /// start a morning on.
    /// </summary>
    public Workspace? SaveWorkspace(string name)
    {
        name = name.Trim();

        var columns = BuildColumns()
            .Select(c => c.Tabs.Where(w => w.Parameters.Count == 0).ToList())
            .Where(tabs => tabs.Count > 0)
            .ToList();

        var keys = columns.SelectMany(tabs => tabs.Select(w => w.Definition.Key)).ToList();
        var indices = columns.SelectMany((tabs, i) => tabs.Select(_ => i)).ToList();

        if (name.Length == 0 || keys.Count == 0)
            return null;

        var workspace = new Workspace(name, Describe(keys.Count, columns.Count), keys,
            columns.Count < keys.Count ? indices : null);

        _userWorkspaces.RemoveAll(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        _userWorkspaces.Add(workspace);
        Notify();
        return workspace;
    }

    /// <summary>"3 windows, saved here", or "5 windows in 3 columns, saved here" when some are tabs.</summary>
    private static string Describe(int windows, int columns)
    {
        var count = $"{windows} {(windows == 1 ? "window" : "windows")}";

        return columns < windows
            ? $"{count} in {columns} {(columns == 1 ? "column" : "columns")}, saved here"
            : $"{count}, saved here";
    }

    public void DeleteWorkspace(string name)
    {
        if (_userWorkspaces.RemoveAll(w => w.Name == name) > 0)
            Notify();
    }

    /// <summary>The desk as it can be written down. See <see cref="DeskLayout"/>.</summary>
    public DeskLayout Capture()
    {
        var columns = BuildColumns();
        var row = columns
            .SelectMany(c => c.Tabs.Select((w, rank) => (Window: w, Column: c.Position, Rank: rank)))
            .ToList();
        var focused = row.FindIndex(p => p.Window.Id == FocusedId);

        return new DeskLayout
        {
            MaxConcurrentWindows = Settings.MaxConcurrentWindows,
            CeilingChosen = _ceilingChosen,
            PrimaryWindowKey = Settings.PrimaryWindowKey,
            PrimaryShare = Settings.PrimaryShare,
            ResolutionMode = Settings.ResolutionMode,
            CustomWidth = Settings.CustomWidth,
            CustomHeight = Settings.CustomHeight,
            UiScale = Settings.UiScale,
            TextScale = Settings.TextScale,
            Windows = row.Select(p => new DeskLayoutWindow(
                p.Window.Definition.Key,
                p.Window.TitleOverride,
                p.Window.Weight,
                p.Window.Minimised,
                p.Window.Parameters.Count == 0
                    ? null
                    : p.Window.Parameters.ToDictionary(x => x.Key, x => System.Text.Json.JsonSerializer.SerializeToElement(x.Value)),
                p.Column,
                p.Rank))
                .ToList(),
            Focused = focused >= 0 ? focused : null,
            Workspaces = _userWorkspaces.Select(w => new DeskLayoutWorkspace(w.Name, w.WindowKeys, w.Columns)).ToList(),
            ActiveWorkspace = _windows.Count > 0 ? ActiveWorkspace?.Name : null,
            ActiveWorkspaceSaved = _windows.Count > 0 && ActiveWorkspace is { } active && _userWorkspaces.Any(w => ReferenceEquals(w, active))
        };
    }

    /// <summary>
    /// Puts a written-down desk back. Windows the catalogue no longer has are dropped rather than
    /// failing the rest — a layout outlives the version of the application that saved it — and so
    /// are windows the person at this desk may not see. A layout from before tab groups gives every
    /// window a column; columns past the limit fold into tabs. Nothing is closed to fit.
    /// </summary>
    public void Restore(DeskLayout layout)
    {
        _windows.Clear();
        FocusedId = null;
        Notice = null;
        NoticeTitles = [];
        _back.Clear();
        _forward.Clear();
        _forgotten = 0;

        _ceilingChosen = layout.CeilingChosen;
        Settings.MaxConcurrentWindows = Math.Clamp(layout.MaxConcurrentWindows, 1, 12);
        Settings.PrimaryWindowKey = layout.PrimaryWindowKey is { } key && Catalog.Find(key) is not null
            ? key
            : layout.PrimaryWindowKey is null ? null : Catalog.DefaultPrimary;
        Settings.PrimaryShare = Math.Clamp(layout.PrimaryShare, 0.2, 0.8);
        Settings.ResolutionMode = layout.ResolutionMode;
        Settings.CustomWidth = layout.CustomWidth > 0 ? layout.CustomWidth : Settings.CustomWidth;
        Settings.CustomHeight = layout.CustomHeight > 0 ? layout.CustomHeight : Settings.CustomHeight;
        Settings.UiScale = DeskSettings.ClampScale(layout.UiScale, DeskSettings.MinUiScale, DeskSettings.MaxUiScale);
        Settings.TextScale = DeskSettings.ClampScale(layout.TextScale, DeskSettings.MinTextScale, DeskSettings.MaxTextScale);

        WindowInstance? focused = null;
        var placed = new List<(WindowInstance Window, int Column, int Rank)>();

        foreach (var (saved, index) in layout.Windows.Select((w, i) => (w, i)))
        {
            if (placed.Count >= DeskSettings.MaxOpenWindows)
                break;

            if (Catalog.Find(saved.Key) is not { } definition)
                continue;

            var instance = new WindowInstance
            {
                Id = $"{definition.Key}-{++_sequence}",
                Definition = definition,
                TitleOverride = saved.TitleOverride,
                Weight = saved.Weight > 0 ? saved.Weight : definition.DefaultWeight,
                Minimised = saved.Minimised,
                LastFocusedAt = DateTimeOffset.UtcNow
            };

            foreach (var (name, value) in saved.Parameters ?? new Dictionary<string, System.Text.Json.JsonElement>())
            {
                if (ToParameter(value) is { } parameter)
                    instance.Parameters[name] = parameter;
            }

            // Before tabs, each window was its own column, in the order written.
            var column = saved.Column ?? index;
            placed.Add((instance, column, saved.Rank ?? placed.Count(p => p.Column == column)));

            if (index == layout.Focused)
                focused = instance;
        }

        Arrange(placed);

        // A column collapses whole, and the one it shows says which way.
        foreach (var column in BuildColumns())
        {
            foreach (var tab in column.Tabs)
                tab.Minimised = column.Showing.Minimised;
        }

        FoldToLimit();
        Notice = null;

        _userWorkspaces.Clear();
        _userWorkspaces.AddRange(layout.Workspaces
            .Where(w => w.WindowKeys.Any(k => Catalog.Find(k) is not null))
            .Select(Readable));

        // Found again by name, in the kind it was written as first (a saved one, or the
        // application's): a layout from before the field reads null and restores no workspace, and
        // one naming a workspace this desk no longer offers — or one a visitor may not open —
        // restores none rather than a stale label.
        IEnumerable<Workspace> lookIn = layout.ActiveWorkspaceSaved
            ? _userWorkspaces.Concat(Catalog.Workspaces)
            : Catalog.Workspaces.Concat(_userWorkspaces);
        ActiveWorkspace = layout.ActiveWorkspace is { } activeName
            ? lookIn.FirstOrDefault(w => w.Name == activeName)
            : null;

        FocusedId = focused is not null && IsShowing(focused)
            ? focused.Id
            : BuildColumns().FirstOrDefault()?.Showing.Id;

        Relayout();
    }

    /// <summary>A saved workspace with the windows this desk cannot open taken out, and its columns with them.</summary>
    private Workspace Readable(DeskLayoutWorkspace saved)
    {
        var keep = saved.WindowKeys.Select((k, i) => (Key: k, Index: i)).Where(k => Catalog.Find(k.Key) is not null).ToList();
        var columns = saved.Columns is { } named && named.Count == saved.WindowKeys.Count
            ? keep.Select(k => named[k.Index]).ToList()
            : null;

        var count = columns?.Distinct().Count() ?? keep.Count;

        return new Workspace(saved.Name, Describe(keep.Count, count), keep.Select(k => k.Key).ToList(), columns);
    }

    /// <summary>A stored parameter back to the type a component expects: ids are ints.</summary>
    private static object? ToParameter(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Number when value.TryGetInt32(out var i) => i,
        System.Text.Json.JsonValueKind.Number when value.TryGetInt64(out var l) => l,
        System.Text.Json.JsonValueKind.Number => value.GetDouble(),
        System.Text.Json.JsonValueKind.String => value.GetString(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => null
    };

    // ------------------------------------------------------------------ history ----

    /// <summary>
    /// How far back the desk remembers. Each step holds references to windows that already
    /// exist plus a copy of their parameters, so this is a bound on surprise, not on memory.
    /// </summary>
    private const int HistoryLimit = 50;

    private readonly List<DeskMoment> _back = [];
    private readonly List<DeskMoment> _forward = [];

    /// <summary>
    /// The desk as it stood before one navigation: which windows were in the row, what each
    /// was showing and the pages it came through, and which had focus. The windows themselves
    /// are kept, not described, so going back puts the same window — same id, same slot, same
    /// width — where it was.
    /// </summary>
    private sealed record DeskMoment(IReadOnlyList<WindowMoment> Row, string? FocusedId, Workspace? ActiveWorkspace = null);

    private sealed record WindowMoment(
        WindowInstance Window,
        WindowDefinition Definition,
        Dictionary<string, object> Parameters,
        string? Title,
        List<WindowPage> BackPages,
        List<WindowPage> ForwardPages,
        string ColumnId,
        long Recency,
        long Sequence,
        bool Minimised,
        bool Maximised,
        double Weight);

    /// <summary>
    /// How many steps Back can take. The host mirrors this into the browser's own history, so
    /// the browser's Back button and the mouse's back key walk the desk rather than leaving it.
    /// </summary>
    public int HistoryDepth => _forgotten + _back.Count;

    /// <summary>Counts steps taken, so the host can tell a new step from a walk back and forth.</summary>
    public long HistoryVersion { get; private set; }

    /// <summary>
    /// Steps dropped off the far end. Counted so <see cref="HistoryDepth"/> keeps rising past the
    /// limit — the browser's entries are numbered by it and would stop matching otherwise.
    /// </summary>
    private int _forgotten;

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// Puts the desk back the way it was before the last open, close or re-aim. A window closed
    /// comes back to its column, a tab to its group; one opened since goes. Before this there was no way
    /// back at all: the desk has one route, so the browser's Back left the application.
    ///
    /// <para>
    /// Not when the step would close or re-aim a window holding unsaved input: then nothing
    /// moves and <see cref="CloseNeedsConfirmation"/> is raised with it, as the close key does.
    /// Back was the one road left by which a half-typed entry went without a question.
    /// </para>
    /// </summary>
    public bool Back()
    {
        if (_back.Count == 0 || Refuses(_back[^1]))
            return false;

        StepBack();
        return true;
    }

    /// <summary>The road Back came down, under the same rule for unsaved input.</summary>
    public bool Forward()
    {
        if (_forward.Count == 0 || Refuses(_forward[^1]))
            return false;

        StepForward();
        return true;
    }

    private void StepBack()
    {
        _forward.Add(Moment());
        Become(Pop(_back));
    }

    private void StepForward()
    {
        _back.Add(Moment());
        Become(Pop(_forward));
    }

    /// <summary>
    /// Whether becoming <paramref name="moment"/> would cost a window its unsaved input, and if
    /// so, says which by raising <see cref="CloseNeedsConfirmation"/>.
    /// </summary>
    private bool Refuses(DeskMoment moment)
    {
        if (Displaced(moment) is not { } window)
            return false;

        Refuse(window);
        return true;
    }

    /// <summary>
    /// Says a step was refused for <paramref name="window"/>'s unsaved input: as a question to
    /// whoever listens (<see cref="CloseNeedsConfirmation"/>), and as the notice, naming the
    /// window. A workspace or a Back that silently did nothing read as a key that was broken; the
    /// notice is how the header, the palette and the browser's Back all say why.
    /// </summary>
    private void Refuse(WindowInstance window)
    {
        Notice = $"Nothing changed: {window.Title} holds input that has not been saved. Save or discard it, then try again.";
        NoticeTitles = [];
        CloseNeedsConfirmation?.Invoke(window);
        Notify();
    }

    /// <summary>
    /// The first window, in row order, holding unsaved input that <paramref name="moment"/> would
    /// close — it is not in that moment — or replace: it is there showing another view, or the
    /// same view on another subject. A window kept as it is passes, whatever else the step does.
    /// </summary>
    private WindowInstance? Displaced(DeskMoment moment)
    {
        foreach (var window in Row().Where(w => w.HasUnsavedInput))
        {
            var saved = moment.Row.FirstOrDefault(m => ReferenceEquals(m.Window, window));

            if (saved is null
                || saved.Definition != window.Definition
                || !SameSubject(window.Parameters, saved.Parameters))
                return window;
        }

        return null;
    }

    /// <summary>Whether two parameter sets name the same subject. A key set to null is a key not named.</summary>
    private static bool SameSubject(Dictionary<string, object> current, Dictionary<string, object> saved)
    {
        var now = current.Where(p => p.Value is not null).ToDictionary();
        var then = saved.Where(p => p.Value is not null).ToDictionary();

        return now.Count == then.Count
            && then.All(p => now.TryGetValue(p.Key, out var value) && Equals(value, p.Value));
    }

    /// <summary>Called before anything that changes what the desk shows. A new step forgets the road ahead.</summary>
    private void Remember()
    {
        _back.Add(Moment());

        if (_back.Count > HistoryLimit)
        {
            _back.RemoveAt(0);
            _forgotten++;
        }

        _forward.Clear();
        HistoryVersion++;

        // A notice's Reopen steps Back, and Back now means this step, not the close the notice
        // was about: the sentence goes with it. A close sets its own after remembering.
        Notice = null;
        NoticeTitles = [];
    }

    /// <summary>
    /// Walks to a depth the browser named — one step for Back, several for a long press on it.
    /// Judged by where the walk ends rather than step by step: a window the walk passes out of
    /// and back into is the same window when it lands, input and all. When the end would close
    /// or re-aim a window holding unsaved input, nothing moves, <see cref="CloseNeedsConfirmation"/>
    /// is raised, and the answer is false.
    /// </summary>
    public bool TraverseTo(int depth)
    {
        var target = depth < HistoryDepth && _back.Count > 0
            ? _back[Math.Max(0, _back.Count - (HistoryDepth - depth))]
            : depth > HistoryDepth && _forward.Count > 0
                ? _forward[Math.Max(0, _forward.Count - (depth - HistoryDepth))]
                : null;

        if (target is not null && Refuses(target))
            return false;

        while (HistoryDepth > depth && _back.Count > 0)
            StepBack();

        while (HistoryDepth < depth && _forward.Count > 0)
            StepForward();

        return true;
    }

    private DeskMoment Moment() => new(
        _windows.Select(w => new WindowMoment(
            w,
            w.Definition,
            new Dictionary<string, object>(w.Parameters),
            w.TitleOverride,
            [.. w.BackPages],
            [.. w.ForwardPages],
            w.ColumnId,
            w.Recency,
            w.Sequence,
            w.Minimised,
            w.Maximised,
            w.Weight)).ToList(),
        FocusedId,
        ActiveWorkspace);

    private void Become(DeskMoment moment)
    {
        _windows.Clear();

        foreach (var saved in moment.Row)
        {
            var window = saved.Window;

            // A window that followed a link since is showing another view, and the parameters of
            // one view are not the parameters of another — a Customer handed an OrderId refuses it.
            // So a different view is put back whole; the same view is re-aimed, as a singleton's
            // subject always was.
            if (window.Definition != saved.Definition)
            {
                window.Definition = saved.Definition;
                window.Parameters.Clear();

                foreach (var (key, value) in saved.Parameters)
                    window.Parameters[key] = value;

                window.PageVersion++;
            }
            else
            {
                Aim(window.Parameters, saved.Parameters);
            }

            window.TitleOverride = saved.Title;
            window.BackPages = [.. saved.BackPages];
            window.ForwardPages = [.. saved.ForwardPages];

            // Its column and its place in it come back too: a tab closed from a group returns to
            // that group, behind the tab it was behind, at the group's width, collapsed or holding
            // the row as the group was. Every tab in a column was saved agreeing, so they agree
            // again. A collapsed window keeps its panel (AppWindow hides the body), so coming
            // back collapsed costs it nothing it holds.
            window.ColumnId = saved.ColumnId;
            window.Recency = saved.Recency;
            window.Sequence = saved.Sequence;
            window.Minimised = saved.Minimised;
            window.Maximised = saved.Maximised;
            window.Weight = saved.Weight;
            window.Touch();
            _windows.Add(window);
        }

        // The limit may have been lowered since the moment was taken.
        FoldToLimit();

        // Focus lands on a window that is drawn: where the moment's focus is outside the column
        // holding the row, the row is given back.
        if (moment.FocusedId is { } held && Find(held) is { } heldWindow)
            GiveRowBackFor(heldWindow);

        FocusedId = moment.FocusedId is { } id && ColumnOf(id) is { } column
            ? column.Showing.Id
            : MostRecent()?.Id;
        Notice = null;
        NoticeTitles = [];
        ActiveWorkspace = moment.ActiveWorkspace;
        Relayout();
    }

    private static T Pop<T>(List<T> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    // ------------------------------------------------------------- window pages ----

    /// <summary>How many pages one window remembers behind it.</summary>
    private const int PageLimit = 30;

    /// <summary>
    /// Follows a link from inside a window: a customer named on an order, a person on a list.
    ///
    /// <para>
    /// A subject window going to another subject turns to it in place, and keeps the page it
    /// left so its own Back key returns there (<see cref="WindowInstance.FollowsInPlace"/>). Every
    /// such link used to open a window of its own — three clicks into a record was three more
    /// columns, each squeezing the rest, and on a full desk each closed something to make room.
    /// The reader's context was the window they were reading; now it stays there.
    /// </para>
    ///
    /// <para>
    /// From anywhere else — a catalogue window, the search, no window at all — this is
    /// <see cref="Open"/>.
    /// </para>
    /// </summary>
    /// <param name="fromId">The window whose link was followed, or null.</param>
    /// <param name="definition">The window to open or turn to.</param>
    /// <param name="parameters">What the window is about, as its component's parameters.</param>
    /// <param name="titleOverride">A title to show instead of the definition's, or null.</param>
    public WindowInstance Follow(
        string? fromId,
        WindowDefinition definition,
        Dictionary<string, object> parameters,
        string? titleOverride = null)
    {
        if (fromId is null
            || Find(fromId) is not { FollowsInPlace: true } from
            || !WindowInstance.Navigable(definition)
            || (definition.OperatorOnly && !Access.CanOperate))
        {
            return Open(definition, parameters, titleOverride);
        }

        if (from.Definition == definition && !Retargets(from, parameters, titleOverride))
        {
            Focus(from.Id);
            return from;
        }

        // Another window already shows this subject: go to it rather than turning this one into
        // a second copy of it.
        if (_windows.FirstOrDefault(w => w != from && w.Definition.Key == definition.Key
                                         && (definition.Singleton || SameSubject(w, parameters))) is { } open)
        {
            Focus(open.Id);
            return open;
        }

        Remember();

        from.BackPages.Add(PageOf(from));

        if (from.BackPages.Count > PageLimit)
            from.BackPages.RemoveAt(0);

        from.ForwardPages.Clear();
        Show(from, new WindowPage(definition, new Dictionary<string, object>(parameters), titleOverride));

        return from;
    }

    /// <summary>Turns a window back to the page it followed a link from.</summary>
    public bool PageBack(string id)
    {
        if (Find(id) is not { CanPageBack: true } window)
            return false;

        Remember();
        window.ForwardPages.Add(PageOf(window));
        Show(window, Pop(window.BackPages));
        return true;
    }

    public bool PageForward(string id)
    {
        if (Find(id) is not { CanPageForward: true } window)
            return false;

        Remember();
        window.BackPages.Add(PageOf(window));
        Show(window, Pop(window.ForwardPages));
        return true;
    }

    /// <summary>What a window is showing, as a page it can come back to. Cleared keys are left out.</summary>
    private static WindowPage PageOf(WindowInstance window) => new(
        window.Definition,
        window.Parameters.Where(p => p.Value is not null).ToDictionary(),
        window.TitleOverride);

    /// <summary>
    /// Puts a page in a window. The parameters are replaced rather than re-aimed, because the
    /// view may have changed, and the page version moves so the host builds the view afresh.
    /// </summary>
    private void Show(WindowInstance window, WindowPage page)
    {
        window.Definition = page.Definition;
        window.Parameters.Clear();

        foreach (var (key, value) in page.Parameters)
            window.Parameters[key] = value;

        window.TitleOverride = page.TitleOverride;
        window.PageVersion++;

        // The next page is a new component; whatever the last one held went with it, and a flag
        // left behind would guard nothing while making every close of it ask a question.
        window.HasUnsavedInput = false;
        window.UnsavedInputLabel = null;

        // The page is shown, so the window is: in front of its column, the column open.
        var column = ColumnOf(window.Id)!;

        foreach (var tab in column.Tabs)
            tab.Minimised = false;

        if (column.Showing != window)
        {
            TouchAll(column.Tabs);
        }

        window.Recency = ++_recency;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        window.Touch();
        FocusedId = window.Id;

        // A customer's view is narrower than an order's: the column's minimum is the new view's.
        Relayout();
    }

    /// <summary>Whether opening <paramref name="window"/> with these arguments would change what it shows.</summary>
    /// <summary>Whether a window is on exactly the subject <paramref name="parameters"/> name: the same keys, the same values.</summary>
    private static bool SameSubject(WindowInstance window, Dictionary<string, object>? parameters)
    {
        var current = window.Parameters.Where(p => p.Value is not null).ToDictionary();
        var asked = (parameters ?? []).Where(p => p.Value is not null).ToDictionary();

        return current.Count == asked.Count
            && asked.All(p => current.TryGetValue(p.Key, out var value) && Equals(value, p.Value));
    }

    private static bool Retargets(WindowInstance window, Dictionary<string, object>? parameters, string? title)
    {
        if (title is not null && title != window.TitleOverride)
            return true;

        if (parameters is null)
            return false;

        var current = window.Parameters.Where(p => p.Value is not null).ToDictionary();
        return current.Count != parameters.Count
            || parameters.Any(p => !current.TryGetValue(p.Key, out var value) || !Equals(value, p.Value));
    }

    /// <summary>
    /// Points a window's parameters at a new subject. A key the new subject does not name is set
    /// to null rather than removed: a hosted component keeps the last value it was given for a
    /// parameter that stops being passed, so removing it would leave the old subject showing.
    /// </summary>
    public static void Aim(Dictionary<string, object> target, IReadOnlyDictionary<string, object> with)
    {
        foreach (var key in target.Keys.Where(k => !with.ContainsKey(k)).ToList())
            target[key] = null!;

        foreach (var (key, value) in with)
            target[key] = value;
    }

    /// <summary>The showing window last shown, in a column that is not collapsed: where focus goes when its window goes.</summary>
    private WindowInstance? MostRecent()
        => BuildColumns()
            .Where(c => !c.Showing.Minimised)
            .Select(c => c.Showing)
            .OrderByDescending(w => w.Recency)
            .FirstOrDefault();

    /// <summary>
    /// Says the desk has changed. Whatever this changed inside a window has already moved that
    /// window's version, so the desk's render redraws those windows and passes the rest by.
    /// </summary>
    private void Notify() => Changed?.Invoke();

    /// <summary>Says one window has changed and nothing else has. See <see cref="WindowChanged"/>.</summary>
    /// <remarks>The column's tab key, drawn by the window its column shows, listens for every tab in it.</remarks>
    private void NotifyWindow(WindowInstance window)
    {
        window.Touch();
        WindowChanged?.Invoke(window.Id);
    }
}
