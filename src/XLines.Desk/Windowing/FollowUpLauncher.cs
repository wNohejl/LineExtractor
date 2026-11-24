namespace XLines.Desk.Windowing;

/// <summary>
/// One follow-up view floating above the desk, opened from a row.
/// </summary>
public sealed class FollowUp
{
    public required string Key { get; init; }

    /// <summary>What the follow-up is about — an order id, a customer id. Part of its identity.</summary>
    public required int SubjectId { get; init; }

    public required WindowDefinition Definition { get; init; }
    public required Dictionary<string, object> Parameters { get; init; }
    public required string Subtitle { get; init; }
    public required string Width { get; init; }

    /// <summary>Raise order. Higher is nearer the front.</summary>
    public int Order { get; set; }

    public string Title => Definition.Title;
    public string Icon => Definition.Icon;
}

/// <summary>
/// Opens a follow-up view as a desk window, falling back to the floating layer when every
/// column is in use.
///
/// <para>
/// A window is the better home: it tiles, it keeps its place in the row, and it survives being
/// scrolled past. Once every column is in use, a window opened from the strip or the palette
/// joins a column as a tab (ADR 0027) — and a tab is the wrong home for a follow-up, which is a
/// look taken from a row the operator is still working in: it would cover another column's
/// window to show one detail of a row. So the column limit stays the switch: while a column is free,
/// follow-ups are windows; once none is, they float above the desk and every column keeps what
/// it shows. So does a desk at its ceiling, where a column may be free but the open would be
/// refused (<see cref="WindowManager.AtCeiling"/>). A follow-up whose window is already on the
/// desk, showing or as a tab, is brought
/// forward and re-aimed there instead (<see cref="WindowManager.Open"/>).
/// </para>
///
/// <para>
/// This used to live inside one list window, which meant that list was the only surface where
/// following something up behaved correctly. It is a launcher rather than a component so the
/// panel keeps ownership of the floating list — dialogs outlive the row that opened them, and a
/// row that owned its own dialogs would take them down the moment the grid re-sorted.
/// </para>
/// </summary>
public sealed class FollowUpLauncher
{
    private readonly List<FollowUp> _open = [];
    private int _raiseCounter;

    /// <summary>
    /// Raised whenever the floating list changes, so the panel drawing it redraws.
    ///
    /// The window half of the switch needs nothing here — <see cref="WindowManager"/> announces
    /// its own changes and the desk listens. The floating half is drawn by the panel that owns
    /// the launcher, and a follow-up is launched from a row action handled inside the grid's
    /// cell: Blazor re-renders the strip that handled the press, not the panel above it. So
    /// mutating the list is invisible unless it says so, and the dialog reads as one that
    /// never opened.
    /// </summary>
    public event Action? Changed;

    /// <summary>The floating follow-ups, in the order they were opened.</summary>
    public IReadOnlyList<FollowUp> Open => _open;

    public bool Any => _open.Count > 0;

    /// <summary>
    /// Opens a follow-up on a subject, as a window if the desk has room and a floating dialog
    /// if it does not.
    /// </summary>
    /// <param name="manager">The desk, consulted for capacity.</param>
    /// <param name="windowKey">Catalog key of the view to open.</param>
    /// <param name="parameterName">The hosted component's id parameter — "GameId", "PlayerId".</param>
    /// <param name="subjectId">The id itself.</param>
    /// <param name="subtitle">Names the subject, e.g. the record's own name. Used as the window title too.</param>
    /// <param name="width">Floating width. Ignored when it opens as a window.</param>
    public void Launch(
        WindowManager manager,
        string windowKey,
        string parameterName,
        int subjectId,
        string subtitle,
        string width = "640px")
        => Launch(manager, windowKey, new Dictionary<string, object> { [parameterName] = subjectId }, subjectId, subtitle, width);

    /// <summary>
    /// Opens a follow-up with several parameters — a record and the side and source a door names.
    /// Re-launching the same view on the same subject while it floats re-aims it with the new
    /// parameters and raises it, rather than stacking a second dialog on top.
    /// </summary>
    public void Launch(
        WindowManager manager,
        string windowKey,
        IReadOnlyDictionary<string, object> parameters,
        int subjectId,
        string subtitle,
        string width = "640px")
    {
        if (manager.Catalog.Find(windowKey) is not { } definition)
            return;

        var copy = new Dictionary<string, object>(parameters);
        var title = $"{definition.Title} · {subtitle}";

        // A singleton already on the desk is re-aimed rather than duplicated (WindowManager.Open),
        // so an order form opened on another record lands on that record rather than on the one
        // the window was already showing — and a tab behind another comes forward to show it.
        // Opening it adds no window, so the desk's capacity does not come into it.
        if (definition.Singleton
            && manager.Windows.Any(w => w.Definition.Key == windowKey))
        {
            manager.Open(definition, copy, title);
            return;
        }

        // At the ceiling Open refuses, so a free column is no home: the follow-up floats instead.
        if (!manager.AtCapacity && !manager.AtCeiling)
        {
            manager.Open(definition, copy, title);
            return;
        }

        // Re-opening the same view for the same subject raises what is already there rather
        // than stacking a duplicate on top of it, with the parameters the new press named.
        if (_open.FirstOrDefault(f => f.Key == windowKey && f.SubjectId == subjectId) is { } existing)
        {
            WindowManager.Aim(existing.Parameters, copy);
            Raise(existing);
            return;
        }

        _open.Add(new FollowUp
        {
            Key = windowKey,
            SubjectId = subjectId,
            Definition = definition,
            Parameters = copy,
            Subtitle = subtitle,
            Width = width,
            Order = ++_raiseCounter
        });

        Notify();
    }

    public void Raise(FollowUp followUp)
    {
        followUp.Order = ++_raiseCounter;
        Notify();
    }

    public void Close(FollowUp followUp)
    {
        if (_open.Remove(followUp))
            Notify();
    }

    /// <summary>Closes every floating follow-up of one kind — used after an action settles.</summary>
    public void CloseAll(string windowKey)
    {
        if (_open.RemoveAll(f => f.Key == windowKey) > 0)
            Notify();
    }

    private void Notify() => Changed?.Invoke();
}
