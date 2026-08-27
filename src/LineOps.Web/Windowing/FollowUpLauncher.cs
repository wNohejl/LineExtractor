namespace LineOps.Web.Windowing;

/// <summary>
/// One follow-up view floating above the desk, opened from a row.
/// </summary>
public sealed class FollowUp
{
    public required string Key { get; init; }

    /// <summary>What the follow-up is about — a game id, a player id. Part of its identity.</summary>
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
/// Opens a follow-up view as a desk window, falling back to the floating layer when the desk
/// is full.
///
/// <para>
/// A window is the better home: it tiles, it keeps its place in the row, and it survives being
/// scrolled past. But the desk enforces its ceiling by <i>evicting</i> the least-recently-used
/// window (ADR 0007), and silently closing the board an operator is working from to make room
/// for a price table is exactly the wrong trade. So the ceiling becomes the switch: while there
/// is room, follow-ups are windows; once there is not, they float above the desk instead of
/// pushing something off it.
/// </para>
///
/// <para>
/// This used to live inside <c>BoardPanel</c>, which meant the Board was the only surface where
/// following something up behaved correctly. It is a launcher rather than a component so the
/// panel keeps ownership of the floating list — dialogs outlive the row that opened them, and a
/// row that owned its own dialogs would take them down the moment the grid re-sorted.
/// </para>
/// </summary>
public sealed class FollowUpLauncher
{
    private readonly List<FollowUp> _open = [];
    private int _raiseCounter;

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
    /// <param name="subtitle">Names the subject, e.g. the matchup. Used as the window title too.</param>
    /// <param name="width">Floating width. Ignored when it opens as a window.</param>
    public void Launch(
        WindowManager manager,
        string windowKey,
        string parameterName,
        int subjectId,
        string subtitle,
        string width = "640px")
    {
        if (WindowCatalog.Find(windowKey) is not { } definition)
            return;

        var parameters = new Dictionary<string, object> { [parameterName] = subjectId };

        if (!manager.AtCapacity)
        {
            manager.Open(definition, parameters, $"{definition.Title} · {subtitle}");
            return;
        }

        // Re-opening the same view for the same subject raises what is already there rather
        // than stacking a duplicate on top of it.
        if (_open.FirstOrDefault(f => f.Key == windowKey && f.SubjectId == subjectId) is { } existing)
        {
            Raise(existing);
            return;
        }

        _open.Add(new FollowUp
        {
            Key = windowKey,
            SubjectId = subjectId,
            Definition = definition,
            Parameters = parameters,
            Subtitle = subtitle,
            Width = width,
            Order = ++_raiseCounter
        });
    }

    public void Raise(FollowUp followUp) => followUp.Order = ++_raiseCounter;

    public void Close(FollowUp followUp) => _open.Remove(followUp);

    /// <summary>Closes every floating follow-up of one kind — used after an action settles.</summary>
    public void CloseAll(string windowKey) => _open.RemoveAll(f => f.Key == windowKey);
}
