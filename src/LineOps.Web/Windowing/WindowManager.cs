namespace LineOps.Web.Windowing;

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
    /// What was just closed to make room, and for what. Eviction must never be silent —
    /// a window vanishing without explanation reads as a bug, not a policy.
    /// </summary>
    public string? LastEviction { get; private set; }

    public void ClearEviction()
    {
        if (LastEviction is null)
            return;

        LastEviction = null;
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
    /// </summary>
    public WindowInstance Open(
        WindowDefinition definition,
        Dictionary<string, object>? parameters = null,
        string? titleOverride = null)
    {
        if (definition.Singleton)
        {
            var existing = _windows.FirstOrDefault(w => w.Definition.Key == definition.Key);
            if (existing is not null)
            {
                existing.Minimised = false;
                Focus(existing.Id);
                return existing;
            }
        }

        var title = titleOverride ?? definition.Title;
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
    /// Closes least-recently-focused windows until <paramref name="headroom"/> slots are free.
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
                .OrderByDescending(w => w.Minimised)
                .ThenBy(w => w.LastFocusedAt)
                .First();

            _windows.Remove(victim);
            evicted.Add(victim.Title);

            if (FocusedId == victim.Id)
                FocusedId = null;
        }

        if (evicted.Count == 0)
            return;

        var closed = string.Join(", ", evicted);

        LastEviction = incoming is null
            ? $"Closed {closed} — over the {Settings.MaxConcurrentWindows}-window limit."
            : $"Closed {closed} to make room for {incoming}.";
    }

    public void Close(string id)
    {
        var window = Find(id);
        if (window is null)
            return;

        _windows.Remove(window);
        LastEviction = null;

        if (FocusedId == id)
            FocusedId = MostRecent()?.Id;

        // Closing a tab gives its space back to the others.
        Relayout();
    }

    public void CloseAll()
    {
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

        var offset = Gap;

        foreach (var w in row)
        {
            if (w.Minimised)
            {
                w.Width = DeskSettings.CollapsedWidth;
            }
            else
            {
                var width = expanded.Count == 0 ? 0 : usable * (w.Weight / totalWeight);

                // A column below its minimum is unreadable; the row is allowed to overflow
                // and the desk scrolls instead, which is honest about not fitting.
                w.Width = Math.Max(w.Definition.MinWidth, width);
            }

            w.X = offset;
            offset += w.Width + Gap;
        }

        Notify();
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

    /// <summary>Opens a named set of windows in order, replacing whatever is on the desk.</summary>
    public void ApplyWorkspace(Workspace workspace)
    {
        _windows.Clear();
        FocusedId = null;
        LastEviction = null;

        // The operator picked this arrangement explicitly, so honour it rather than evicting
        // half of it on the way in.
        if (workspace.WindowKeys.Count > Settings.MaxConcurrentWindows)
            Settings.MaxConcurrentWindows = workspace.WindowKeys.Count;

        foreach (var key in workspace.WindowKeys)
        {
            var definition = WindowCatalog.Find(key);
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
    }

    public WindowInstance? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    private WindowInstance? MostRecent()
        => _windows.Where(w => !w.Minimised).OrderByDescending(w => w.LastFocusedAt).FirstOrDefault();

    private void Notify() => Changed?.Invoke();
}
