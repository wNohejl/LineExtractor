using XLines.Desk.Windowing;

namespace XLines.Web.Windowing;

/// <summary>
/// The Line watch workspace's one rule (ADR 0029): while it is the workspace on the desk, a game
/// pressed on the Board is shown in Line movement — the window already open, re-aimed and brought
/// forward — rather than only opening the row's own answer.
/// </summary>
/// <remarks>
/// The workspace is known by its <see cref="Workspace.Tag"/>, which the Desk carries and never
/// reads, so renaming the workspace or rewording its description changes nothing here. The rule
/// holds while <see cref="WindowManager.ActiveWorkspace"/> is Line watch and the workspace's own Line
/// movement window is on the desk; closing it ends the watch, as emptying the desk or applying
/// another workspace does.
/// </remarks>
public static class LineWatch
{
    /// <summary>The tag the Line watch workspace carries (<see cref="WindowCatalog.Workspaces"/>).</summary>
    public const string Tag = "line-watch";

    /// <summary>
    /// What the Board says while the rule holds, so a press is never a surprise: one line, the
    /// label of a disclosure that holds the rest (<see cref="BoardHintMore"/>).
    /// </summary>
    public const string BoardHint = "Line watch: pressing a game shows it in Line movement.";

    /// <summary>The rest of the Board's note, one press away behind <see cref="BoardHint"/>.</summary>
    public const string BoardHintMore =
        "Pressing a price shows that market and side. Place wager on the open row takes the price last pressed.";

    /// <summary>
    /// Whether the rule holds: Line watch is the workspace on the desk and its own Line movement
    /// window — the one the workspace opened — is still on it. The Desk keeps a workspace as the
    /// active one while its windows are closed and others opened, and restores it with a saved
    /// layout, so the tag alone would go on taking the Board's presses long after the desk stopped
    /// being a line watch. Closing the workspace's Line movement window ends the watch: a Line
    /// movement window opened afterwards from the strip is a window of its own, which opens on the
    /// next game and is not driven by the Board. Applying Line watch again starts a new watch.
    /// </summary>
    public static bool IsActive(WindowManager manager) => Watched(manager).Count > 0;

    /// <summary>
    /// Whether this window is the one the watch drives: its overview and its following. A second
    /// Line movement window on the same desk is standalone. Without a window id (a window rendered
    /// on its own, as in a test), whether the watch holds at all.
    /// </summary>
    public static bool Drives(WindowManager manager, string? windowId)
        => windowId is null ? IsActive(manager) : Watched(manager).Contains(windowId);

    /// <summary>
    /// The watch's Line movement window a press should drive: the one showing in its column if any
    /// is, else the one last in front. Null when the watch does not hold.
    /// </summary>
    public static WindowInstance? Target(WindowManager manager)
    {
        var watched = Watched(manager);

        return manager.Windows
            .Where(w => watched.Contains(w.Id))
            .OrderByDescending(manager.IsShowing)
            .ThenByDescending(w => w.LastFocusedAt)
            .FirstOrDefault();
    }

    /// <summary>One desk's watch: the Line movement windows it drives, and the desk it ended on.</summary>
    private sealed class Watch
    {
        public readonly HashSet<string> Drives = [];

        /// <summary>The windows on the desk when the watch ended; null while it holds.</summary>
        public HashSet<string>? EndedAmong;
    }

    // Per desk (a WindowManager is one circuit's), held weakly so a closed circuit takes its watch with it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WindowManager, Watch> Watches = new();

    /// <summary>
    /// The watch's Line movement windows, worked out from the desk as it stands. The first time the
    /// desk is seen as a line watch, the Line movement windows on it are the workspace's and are
    /// adopted. When the last of them is gone the watch has ended, and stays ended while any window
    /// of that desk remains; a desk whose windows are all new while still tagged Line watch has had
    /// the workspace applied again (applying it replaces every window), so the watch starts afresh.
    /// </summary>
    private static IReadOnlySet<string> Watched(WindowManager manager)
    {
        if (manager.ActiveWorkspace?.Tag != Tag)
        {
            Watches.Remove(manager);
            return EmptyIds;
        }

        var watch = Watches.GetOrCreateValue(manager);

        lock (watch)
        {
            var ids = manager.Windows.Select(w => w.Id).ToHashSet();

            if (watch.EndedAmong is { } among)
            {
                if (among.Overlaps(ids))
                    return EmptyIds;

                watch.EndedAmong = null;
                watch.Drives.Clear();
            }

            if (watch.Drives.Count == 0)
            {
                foreach (var odds in manager.Windows.Where(w => w.Definition.Key == WindowCatalog.Odds))
                    watch.Drives.Add(odds.Id);
            }

            watch.Drives.IntersectWith(ids);

            if (watch.Drives.Count == 0)
            {
                watch.EndedAmong = ids;
                return EmptyIds;
            }

            return watch.Drives.ToHashSet();
        }
    }

    private static readonly IReadOnlySet<string> EmptyIds = new HashSet<string>();

    // A token per press, handed to the window (OddsPanel.Press): pressing the same price twice is two
    // aims, so the second turns the window back after the operator looked elsewhere in it. Each is
    // kept with the league day it was issued on, so a window can tell today's press from one a saved
    // desk brought back from an earlier day — a choice nobody made today (IsCurrent). Shared by every
    // circuit on this server: a token is a random GUID, and one desk cannot guess another's.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateOnly> Issued = new();

    /// <summary>The most tokens kept; past it, the oldest days go first.</summary>
    private const int MostIssued = 4096;

    /// <summary>A new press token, today's; older days' tokens are let go of as new ones are issued.</summary>
    public static string Issue()
    {
        var today = XLines.Core.Analytics.LeagueClock.Today();

        foreach (var stale in Issued.Where(t => t.Value < today).Select(t => t.Key).ToList())
            Issued.TryRemove(stale, out _);

        if (Issued.Count >= MostIssued)
        {
            foreach (var extra in Issued.Keys.Take(Issued.Count - MostIssued + 1).ToList())
                Issued.TryRemove(extra, out _);
        }

        var token = Guid.NewGuid().ToString("N");
        Issued[token] = today;
        return token;
    }

    /// <summary>
    /// Whether a press token was issued today, on this server. A desk restored from an earlier day,
    /// or from an earlier run, carries a token that is not today's.
    /// </summary>
    public static bool IsCurrent(object? press)
        => press?.ToString() is { Length: > 0 } token
           && Issued.TryGetValue(token, out var day)
           && day == XLines.Core.Analytics.LeagueClock.Today();

    /// <summary>
    /// Shows a game — and, from a price, its market and side — in the Line movement window on the
    /// desk, re-aimed in place. Never opens one: with none on the desk the rule does not hold
    /// (<see cref="IsActive"/>) and nothing happens. Brought forward if it is a tab behind another
    /// in a column of its own; in the Board's own column it is aimed but left behind the Board, so
    /// a press never hides the slate it was made on. The Board keeps the desk's focus.
    /// </summary>
    /// <param name="fromWindowId">The Board's own window, which keeps the desk's focus.</param>
    public static WindowInstance? Show(WindowManager manager, int gameId, string? market, string? outcome,
        string title, string? fromWindowId)
    {
        if (Target(manager) is not { } odds)
            return null;

        // A row press names only the game: the window keeps the market and side it was on.
        var parameters = new Dictionary<string, object>
        {
            ["GameId"] = gameId,
            ["Press"] = Issue()
        };
        if (market is not null)
            parameters["Market"] = market;
        if (outcome is not null)
            parameters["Outcome"] = outcome;

        WindowManager.Aim(odds.Parameters, parameters);
        odds.TitleOverride = title;

        var from = fromWindowId is null ? null : manager.Find(fromWindowId);
        var shared = from is not null && manager.ColumnOf(from.Id)?.Tabs.Contains(odds) == true;

        // Focus redraws the window with what it was just aimed at, and brings it forward. Then the
        // desk's focus goes back to the Board — which, sharing a column, also puts the Board back
        // in front, Line movement aimed and waiting one tab behind it.
        manager.Focus(odds.Id);

        if (from is not null && (shared || manager.FocusedId != from.Id))
            manager.Focus(from.Id);

        return odds;
    }
}
