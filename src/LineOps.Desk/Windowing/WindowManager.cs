namespace LineOps.Desk.Windowing;

/// <summary>
/// Owns every open window: the row order, widths, and focus.
///
/// Scoped per circuit, so each browser session has its own desk. State lives here rather
/// than in the components so that every surface — header, windows, dividers — reads one
/// source of truth.
///
/// The desk is always a horizontal row of full-height columns, and every window is its own
/// tab in that row: there is no separate taskbar or chip list anywhere else, and no free
/// placement. Reordering the row (dragging a tab past a neighbour) and resizing it (dragging
/// a divider) are the only two gestures, and they are deliberately distinct hit targets so
/// neither is ever triggered by accident.
/// </summary>
public class WindowManager
{
    /// <summary>Gap between columns and around the row, in CSS pixels.</summary>
    private const double Gap = 8;

    private readonly List<WindowInstance> _windows = [];
    private long _sequence;

    /// <summary>
    /// True once the operator has set the ceiling themselves, after which the first viewport
    /// measurement stops overriding it.
    /// </summary>
    private bool _ceilingChosen;

    private bool _viewportMeasured;

    /// <summary>Raised whenever anything the UI renders has changed.</summary>
    public event Action? Changed;

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

    public IReadOnlyList<WindowInstance> Windows => _windows;

    public DeskSettings Settings { get; } = new();

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

    public int OpenCount => _windows.Count;

    public bool AtCapacity => OpenCount >= Settings.MaxConcurrentWindows;

    /// <summary>
    /// What was just closed — to make room, or by its own close key — said as a sentence the
    /// header shows with a key to bring it back. Eviction must never be silent: a window
    /// vanishing without explanation reads as a bug, not a policy.
    /// </summary>
    public string? LastEviction { get; private set; }

    /// <summary>
    /// The titles behind <see cref="LastEviction"/>, so a key that offers to bring them back can
    /// name them without reading the sentence apart. Empty when the notice is a refusal.
    /// </summary>
    public IReadOnlyList<string> LastEvictedTitles { get; private set; } = [];

    public void ClearEviction()
    {
        if (LastEviction is null)
            return;

        LastEviction = null;
        LastEvictedTitles = [];
        Notify();
    }

    public void UpdateSettings(Action<DeskSettings> mutate)
    {
        var before = Settings.MaxConcurrentWindows;
        mutate(Settings);

        if (Settings.MaxConcurrentWindows != before)
            _ceilingChosen = true;

        // The ceiling may have dropped below what is open; trim before relaying out.
        EnforceCeiling(null);
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
                Settings.MaxConcurrentWindows = Math.Max(2, RecommendedCapacity);

            Relayout();
            return;
        }

        if (changed)
            Relayout();
    }

    /// <summary>
    /// Opens a window, closing the least recently used one first if the desk is full.
    ///
    /// Eviction rather than refusal: the operator asked for this window, so they get it.
    /// The cost is that something goes away, which is why the victim is always the one they
    /// have ignored longest and why the footer names it.
    ///
    /// <para>
    /// Never a window holding unsaved input: a half-typed entry is lost for good, and a form the
    /// operator stepped away from is precisely the one least recently used. When nothing else can
    /// go, the open is refused instead — <see cref="LastEviction"/> says why, and what comes back
    /// is the window holding the input, focused, since that is what has to be dealt with first.
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

                existing.Minimised = false;
                Focus(existing.Id);
                return existing;
            }
        }

        var title = titleOverride ?? definition.Title;

        // Decided before anything closes: evicting the clean windows and then finding the rest
        // dirty would lose them for an open that does not happen.
        if (Blocker(headroom: 1) is { } blocker)
        {
            LastEviction = $"Every window holds unsaved input; save or discard one to open {title}.";
            LastEvictedTitles = [];
            Focus(blocker.Id);
            return blocker;
        }

        Remember();
        EnforceCeiling(incoming: title, headroom: 1);

        var instance = new WindowInstance
        {
            Id = $"{definition.Key}-{++_sequence}",
            Sequence = _sequence,
            Definition = definition,
            TitleOverride = titleOverride,
            Weight = definition.DefaultWeight,
            LastFocusedAt = DateTimeOffset.UtcNow
        };

        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
                instance.Parameters[key] = value;
        }

        _windows.Add(instance);
        FocusedId = instance.Id;

        // Appending is the whole interaction: the new tab takes its place at the end of the
        // row and the existing ones give up the space for it.
        Relayout();

        return instance;
    }

    /// <summary>
    /// The window that stops <paramref name="headroom"/> slots being freed, or null when there
    /// are enough windows without unsaved input to close. The one ignored longest, so the
    /// operator is shown the input they have most likely forgotten.
    /// </summary>
    private WindowInstance? Blocker(int headroom)
    {
        var needed = _windows.Count + headroom - Settings.MaxConcurrentWindows;

        if (needed <= 0 || _windows.Count(w => !w.HasUnsavedInput) >= needed)
            return null;

        return _windows
            .Where(w => w.HasUnsavedInput)
            .OrderBy(w => w.LastFocusedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Closes least-recently-focused windows until <paramref name="headroom"/> slots are free.
    /// A window holding unsaved input is passed by; when only those are left the desk stays over
    /// its ceiling rather than lose what was typed.
    /// </summary>
    private void EnforceCeiling(string? incoming, int headroom = 0)
    {
        var evicted = new List<string>();

        while (_windows.Count + headroom > Settings.MaxConcurrentWindows && _windows.Count > 0)
        {
            // Collapsed windows are the most expendable — the operator has already set them
            // aside — then oldest focus wins. Row position is never a factor: closing the
            // left-most tab would punish whatever happened to open first, not whatever is
            // actually stale.
            var victim = _windows
                .Where(w => !w.HasUnsavedInput)
                .OrderByDescending(w => w.Minimised)
                .ThenBy(w => w.LastFocusedAt)
                .FirstOrDefault();

            if (victim is null)
                break;

            _windows.Remove(victim);
            evicted.Add(victim.Title);

            if (FocusedId == victim.Id)
                FocusedId = null;
        }

        if (evicted.Count == 0)
            return;

        var closed = string.Join(", ", evicted);
        LastEvictedTitles = evicted;

        LastEviction = incoming is null
            ? $"Closed {closed} — over the {Settings.MaxConcurrentWindows}-window limit."
            : $"Closed {closed} to make room for {incoming}.";
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

    /// <summary>Closes a window whatever it holds. See <see cref="TryClose"/>.</summary>
    public void Close(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        Remember();
        _windows.Remove(window);

        // Said the way an eviction is, so the way back is a key that names the window. The
        // header's Back arrow used to be that way back; it moved onto the windows, where it
        // walks a window's own pages, and a window closed by its own key has no window left to
        // carry it.
        LastEviction = $"Closed {window.Title}.";
        LastEvictedTitles = [window.Title];

        if (FocusedId == id)
            FocusedId = MostRecent()?.Id;

        // Closing a tab gives its space back to the others.
        Relayout();
    }

    /// <summary>Closes every window whatever they hold. See <see cref="TryCloseAll"/>.</summary>
    public void CloseAll()
    {
        if (_windows.Count > 0)
            Remember();

        _windows.Clear();
        FocusedId = null;
        LastEviction = null;
        Notify();
    }

    public void Focus(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        var wasMinimised = window.Minimised;

        window.Minimised = false;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        FocusedId = id;

        // Expanding changes how width is shared, so it must be recomputed. A plain focus
        // does not — the row keeps its order and sizes, which is what makes a tiled desk
        // stable to work in.
        if (wasMinimised)
            Relayout();
        else
            Notify();
    }

    /// <summary>Collapses a window to a title-only tab, or restores one that already is.</summary>
    public void ToggleMinimise(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        if (window.Minimised)
        {
            Focus(id);
            return;
        }

        window.Minimised = true;

        if (FocusedId == id)
            FocusedId = MostRecent()?.Id;

        Relayout();
    }

    public void ToggleMaximise(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        var maximise = !window.Maximised;

        // Only one window can hold the row at a time.
        foreach (var w in _windows)
            w.Maximised = false;

        window.Maximised = maximise;

        // A collapsed window has nothing to maximise into — expand it first, matching what
        // clicking its tab would do anyway.
        window.Minimised = false;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        FocusedId = id;

        Relayout();
    }

    /// <summary>
    /// Reassigns row order from a dragged tab's drop position. Every window not mentioned
    /// keeps its relative order; this is called with the complete row, so that case does
    /// not arise in practice.
    /// </summary>
    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        var position = 0;

        foreach (var id in orderedIds)
        {
            if (Find(id) is { } window)
                window.Sequence = position++;
        }

        Relayout();
    }

    /// <summary>
    /// Applies a divider drag: the two adjacent columns swap width between them and nothing
    /// else in the row moves. Their combined weight is preserved, so the rest of the row is
    /// untouched — which is what makes a divider feel local rather than global.
    /// </summary>
    public void AdjustSplit(string leftId, string rightId, double leftWidthPx, double rightWidthPx)
    {
        var left = Find(leftId);
        var right = Find(rightId);

        if (left is null || right is null)
            return;

        var totalWeight = left.Weight + right.Weight;
        var totalPx = leftWidthPx + rightWidthPx;

        if (totalPx <= 0 || totalWeight <= 0)
            return;

        left.Weight = totalWeight * (leftWidthPx / totalPx);
        right.Weight = totalWeight - left.Weight;

        Relayout();
    }

    /// <summary>Returns every column to the width the primary-window setting implies.</summary>
    public void ResetWidths()
    {
        foreach (var w in _windows)
            w.Weight = w.Definition.DefaultWeight;

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
        Notify();
    }

    /// <summary>Lets a hosted panel report its own state up to its window chrome.</summary>
    public void SetPulse(string id, PulseState pulse, string? statusText = null)
    {
        var window = Find(id);
        if (window is null)
            return;

        if (window.Pulse == pulse && window.StatusText == statusText)
            return;

        window.Pulse = pulse;
        window.StatusText = statusText;
        Notify();
    }

    /// <summary>
    /// Lays every window out in one horizontal row, left to right in <see cref="WindowInstance.Sequence"/>
    /// order. Collapsed windows take a fixed slim width; the rest split what remains by weight.
    /// The only layout there is.
    /// </summary>
    public void Relayout()
    {
        var row = Row();

        if (row.Count == 0)
        {
            Notify();
            return;
        }

        if (row.FirstOrDefault(w => w.Maximised) is { } maximised)
        {
            // Only the maximised window is rendered, so the others' geometry is irrelevant;
            // keeping it sane avoids a jump when it is restored.
            foreach (var w in row)
            {
                w.X = Gap;
                w.Width = DeskWidth - Gap * 2;
            }

            maximised.X = Gap;
            maximised.Width = DeskWidth - Gap * 2;

            Notify();
            return;
        }

        var expanded = row.Where(w => !w.Minimised).ToList();
        var collapsedCount = row.Count - expanded.Count;

        ApplyPrimaryShare(expanded);

        var usable = DeskWidth - Gap * (row.Count + 1) - collapsedCount * DeskSettings.CollapsedWidth;
        var totalWeight = expanded.Sum(w => w.Weight);

        if (totalWeight <= 0)
            totalWeight = Math.Max(expanded.Count, 1);

        var widths = ColumnWidths(expanded, Math.Max(usable, 0), totalWeight);
        var offset = Gap;

        foreach (var w in row)
        {
            w.Width = w.Minimised ? DeskSettings.CollapsedWidth : widths[w];

            w.X = offset;
            offset += w.Width + Gap;
        }

        Notify();
    }

    /// <summary>
    /// Splits the row's usable width between the expanded columns by weight, and the row
    /// always sums to that width — it never runs past the desk.
    ///
    /// <para>
    /// A column whose share falls under its minimum is lifted to the minimum and the rest
    /// divide what is left, so a minimum costs its neighbours rather than the desk's edge.
    /// When the minimums together do not fit — two columns at 800px, one of them the Board's
    /// 680 — they cannot all be honoured, and every column takes its plain share instead and
    /// scrolls inside itself. Overflowing the row instead put the last window's right half
    /// off-screen behind a sideways scroll, which is the arrangement capacity exists to avoid.
    /// </para>
    /// </summary>
    private static Dictionary<WindowInstance, double> ColumnWidths(
        List<WindowInstance> expanded, double usable, double totalWeight)
    {
        var widths = new Dictionary<WindowInstance, double>();

        if (expanded.Count == 0)
            return widths;

        double Share(WindowInstance w, double room, double weight)
            => weight > 0 ? room * (w.Weight / weight) : room / expanded.Count;

        if (expanded.Sum(w => w.Definition.MinWidth) > usable)
        {
            foreach (var w in expanded)
                widths[w] = Share(w, usable, totalWeight);

            return widths;
        }

        var free = new List<WindowInstance>(expanded);
        var room = usable;

        // Lift every column under its minimum, then re-divide among the rest; lifting one can
        // push another under, so repeat until nothing more is lifted. Terminates: each pass
        // removes at least one column or stops.
        while (true)
        {
            var weight = free.Sum(w => w.Weight);
            var under = free.Where(w => Share(w, room, weight) < w.Definition.MinWidth).ToList();

            if (under.Count == 0)
            {
                foreach (var w in free)
                    widths[w] = Share(w, room, weight);

                return widths;
            }

            foreach (var w in under)
            {
                widths[w] = w.Definition.MinWidth;
                room -= w.Definition.MinWidth;
                free.Remove(w);
            }
        }
    }

    /// <summary>
    /// Every window, left to right in row order. Includes collapsed tabs — there is nowhere
    /// else for them to be, since the row is the only list of open windows there is.
    /// </summary>
    private List<WindowInstance> Row() => _windows.OrderBy(w => w.Sequence).ToList();

    /// <summary>
    /// Nudges the primary window's weight so it lands on its configured share, unless the
    /// operator has since dragged a divider — a manual adjustment outranks a default. Only
    /// affects width: the primary window's position in the row is whatever the operator put
    /// it at, same as any other tab.
    /// </summary>
    private void ApplyPrimaryShare(List<WindowInstance> expanded)
    {
        if (Settings.PrimaryWindowKey is not { } key || expanded.Count < 2)
            return;

        var primary = expanded.FirstOrDefault(w => w.Definition.Key == key);
        if (primary is null)
            return;

        // Untouched columns all sit at their default weight; if any differ, widths are the
        // operator's and this leaves them alone.
        var others = expanded.Where(w => w != primary).ToList();
        if (others.Any(w => Math.Abs(w.Weight - w.Definition.DefaultWeight) > 0.001))
            return;

        var share = Math.Clamp(Settings.PrimaryShare, 0.2, 0.8);
        var othersWeight = others.Sum(w => w.Weight);

        primary.Weight = othersWeight * share / (1 - share);
    }

    /// <summary>
    /// Opens a named set of windows in order, replacing whatever is on the desk — unless a window
    /// on it holds unsaved input, in which case nothing changes and
    /// <see cref="CloseNeedsConfirmation"/> is raised with the first such window. A workspace
    /// replaces every window, so one typed-in window stops the lot, as Close all does. True when
    /// the workspace is on the desk.
    /// </summary>
    public bool ApplyWorkspace(Workspace workspace)
    {
        if (Row().FirstOrDefault(w => w.HasUnsavedInput) is { } dirty)
        {
            CloseNeedsConfirmation?.Invoke(dirty);
            return false;
        }

        if (_windows.Count > 0)
            Remember();

        _windows.Clear();
        FocusedId = null;
        LastEviction = null;

        // The operator picked this arrangement explicitly, so honour it rather than evicting
        // half of it on the way in.
        if (workspace.WindowKeys.Count > Settings.MaxConcurrentWindows)
            Settings.MaxConcurrentWindows = workspace.WindowKeys.Count;

        foreach (var key in workspace.WindowKeys)
        {
            var definition = Catalog.Find(key);
            if (definition is null)
                continue;

            _windows.Add(new WindowInstance
            {
                Id = $"{definition.Key}-{++_sequence}",
                Sequence = _sequence,
                Definition = definition,
                Weight = definition.DefaultWeight,
                LastFocusedAt = DateTimeOffset.UtcNow
            });
        }

        FocusedId = _windows.FirstOrDefault()?.Id;
        Relayout();
        return true;
    }

    public WindowInstance? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    private readonly List<Workspace> _userWorkspaces = [];

    /// <summary>Workspaces the operator saved on this machine, after the application's own.</summary>
    public IReadOnlyList<Workspace> UserWorkspaces => _userWorkspaces;

    /// <summary>
    /// Saves the row as a named workspace, replacing one of the same name. Only windows that
    /// open on nothing go in: a workspace is a starting arrangement, and "this order's history"
    /// is not something to start a morning on.
    /// </summary>
    public Workspace? SaveWorkspace(string name)
    {
        name = name.Trim();
        var keys = _windows
            .Where(w => w.Parameters.Count == 0)
            .OrderBy(w => w.Sequence)
            .Select(w => w.Definition.Key)
            .ToList();

        if (name.Length == 0 || keys.Count == 0)
            return null;

        var workspace = new Workspace(name, $"{keys.Count} {(keys.Count == 1 ? "window" : "windows")}, saved here", keys);

        _userWorkspaces.RemoveAll(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        _userWorkspaces.Add(workspace);
        Notify();
        return workspace;
    }

    public void DeleteWorkspace(string name)
    {
        if (_userWorkspaces.RemoveAll(w => w.Name == name) > 0)
            Notify();
    }

    /// <summary>The desk as it can be written down. See <see cref="DeskLayout"/>.</summary>
    public DeskLayout Capture()
    {
        var row = _windows.OrderBy(w => w.Sequence).ToList();
        var focused = row.FindIndex(w => w.Id == FocusedId);

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
            Windows = row.Select(w => new DeskLayoutWindow(
                w.Definition.Key,
                w.TitleOverride,
                w.Weight,
                w.Minimised,
                w.Parameters.Count == 0
                    ? null
                    : w.Parameters.ToDictionary(p => p.Key, p => System.Text.Json.JsonSerializer.SerializeToElement(p.Value))))
                .ToList(),
            Focused = focused >= 0 ? focused : null,
            Workspaces = _userWorkspaces.Select(w => new DeskLayoutWorkspace(w.Name, w.WindowKeys)).ToList()
        };
    }

    /// <summary>
    /// Puts a written-down desk back. Windows the catalogue no longer has are dropped rather than
    /// failing the rest — a layout outlives the version of the application that saved it.
    /// </summary>
    public void Restore(DeskLayout layout)
    {
        _windows.Clear();
        FocusedId = null;
        LastEviction = null;
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

        string? focusedId = null;

        foreach (var (saved, index) in layout.Windows.Select((w, i) => (w, i)))
        {
            if (Catalog.Find(saved.Key) is not { } definition)
                continue;

            var instance = new WindowInstance
            {
                Id = $"{definition.Key}-{++_sequence}",
                Sequence = _sequence,
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

            _windows.Add(instance);

            if (index == layout.Focused)
                focusedId = instance.Id;
        }

        EnforceCeiling(null);
        LastEviction = null;

        _userWorkspaces.Clear();
        _userWorkspaces.AddRange(layout.Workspaces
            .Where(w => w.WindowKeys.Any(k => Catalog.Find(k) is not null))
            .Select(w => new Workspace(w.Name, $"{w.WindowKeys.Count} windows, saved here",
                w.WindowKeys.Where(k => Catalog.Find(k) is not null).ToList())));

        FocusedId = focusedId ?? _windows.FirstOrDefault()?.Id;
        Relayout();
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
    private sealed record DeskMoment(IReadOnlyList<WindowMoment> Row, string? FocusedId);

    private sealed record WindowMoment(
        WindowInstance Window,
        WindowDefinition Definition,
        Dictionary<string, object> Parameters,
        string? Title,
        List<WindowPage> BackPages,
        List<WindowPage> ForwardPages);

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
    /// to make room comes back; the one that displaced it goes. Before this there was no way
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

        CloseNeedsConfirmation?.Invoke(window);
        return true;
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
            [.. w.ForwardPages])).ToList(),
        FocusedId);

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
            _windows.Add(window);
        }

        FocusedId = moment.FocusedId is { } id && Find(id) is not null ? id : MostRecent()?.Id;
        LastEviction = null;
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
    /// such link used to open a window of its own — three clicks into a matchup was three more
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
        // left behind would guard nothing while keeping the window from ever being evicted.
        window.HasUnsavedInput = false;
        window.UnsavedInputLabel = null;
        window.Minimised = false;
        window.LastFocusedAt = DateTimeOffset.UtcNow;
        FocusedId = window.Id;

        // A customer's view is narrower than an order's: the column's minimum is the new view's.
        Relayout();
    }

    /// <summary>Whether opening <paramref name="window"/> with these arguments would change what it shows.</summary>
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

    private WindowInstance? MostRecent()
        => _windows.Where(w => !w.Minimised).OrderByDescending(w => w.LastFocusedAt).FirstOrDefault();

    private void Notify() => Changed?.Invoke();
}
