using LineOps.Web.Components.Panels;
using MudBlazor;

namespace LineOps.Web.Windowing;

/// <summary>
/// Every window the desk can open, and the named workspaces that arrange them.
///
/// A new window is one entry here — the header, taskbar and host all read this list, so
/// nothing else needs touching. That is the "every sub-page becomes a window" property:
/// panels are ordinary components and know nothing about being windowed.
/// </summary>
public static class WindowCatalog
{
    public const string Dashboard = "dashboard";
    public const string Odds = "odds";
    public const string Players = "players";
    public const string Journal = "journal";
    public const string Performance = "performance";
    public const string Ops = "ops";
    public const string Incidents = "incidents";
    public const string Runs = "runs";
    public const string Desk = "desk";
    public const string Parts = "parts";
    public const string History = "history";
    public const string Board = "board";

    // The board's three follow-ups. Each takes a GameId, so several can be open against
    // different games at once — which is the workflow the board exists to serve.
    public const string Bets = "bets";
    public const string Wager = "wager";
    public const string Form = "form";

    // What clicking a game or a team resolves to, from anywhere on the desk.
    public const string Game = "game";
    public const string Team = "team";

    public static readonly IReadOnlyList<WindowDefinition> All =
    [
        new()
        {
            Key = Board,
            Title = "Board",
            Icon = Icons.Material.Filled.Leaderboard,
            Group = "Data",
            ComponentType = typeof(BoardPanel),
            Description = "Best price on every market, and which book has it.",
            // The widest thing on the desk: three markets, two sides each, plus the rails.
            DefaultWeight = 1.8,
            MinWidth = 620
        },
        new()
        {
            Key = Bets,
            Title = "Every book",
            Icon = Icons.Material.Filled.ViewList,
            Group = "Data",
            ComponentType = typeof(BoardAllBets),
            Description = "Every book's number on every market for one game.",
            DefaultWeight = 1.1,
            MinWidth = 420
        },
        new()
        {
            Key = Wager,
            Title = "Place wager",
            Icon = Icons.Material.Filled.Bolt,
            Group = "Data",
            ComponentType = typeof(BoardWager),
            Description = "Log a bet against the best price on the board.",
            // A form, not a table — it reads fine narrow.
            DefaultWeight = 0.8,
            MinWidth = 340
        },
        new()
        {
            Key = Form,
            Title = "Recent form",
            Icon = Icons.Material.Filled.QueryStats,
            Group = "Data",
            ComponentType = typeof(BoardForm),
            Description = "Both rosters' recent form, read from the stats history.",
            DefaultWeight = 1.3,
            MinWidth = 460
        },
        new()
        {
            Key = Game,
            Title = "Game",
            Icon = Icons.Material.Filled.SportsScore,
            Group = "Data",
            ComponentType = typeof(GamePanel),
            Description = "Lines, team data and players for one game — live score and top bets first while it's being played.",
            // The widest single-game view: lines plus two rosters worth reading, not scanning.
            DefaultWeight = 1.6,
            MinWidth = 560,
            // Comparing two games — or a game against the one that follows it — is normal.
            Singleton = false
        },
        new()
        {
            Key = Team,
            Title = "Team",
            Icon = Icons.Material.Filled.Shield,
            Group = "Data",
            ComponentType = typeof(TeamPanel),
            Description = "A team's recent record and roster form.",
            DefaultWeight = 1.0,
            MinWidth = 380,
            Singleton = false
        },
        new()
        {
            Key = Dashboard,
            Title = "Slate",
            Icon = Icons.Material.Filled.ViewAgenda,
            Group = "Data",
            ComponentType = typeof(DashboardPanel),
            Description = "Today's games and the current market.",
            // A wide table; give it more of the row than a settings pane needs.
            DefaultWeight = 1.3,
            MinWidth = 420
        },
        new()
        {
            Key = Odds,
            Title = "Line movement",
            Icon = Icons.Material.Filled.ShowChart,
            Group = "Data",
            ComponentType = typeof(OddsPanel),
            Description = "How a game's price moved, per book.",
            DefaultWeight = 1.2,
            MinWidth = 380,
            // Not a singleton: comparing two games side by side is the point.
            Singleton = false
        },
        new()
        {
            Key = Players,
            Title = "Players",
            Icon = Icons.Material.Filled.Groups,
            Group = "Data",
            ComponentType = typeof(PlayersPanel),
            Description = "Rosters and per-game statistics.",
            DefaultWeight = 1.1,
            MinWidth = 360
        },
        new()
        {
            Key = Journal,
            Title = "Journal",
            Icon = Icons.Material.Filled.MenuBook,
            Group = "Analytics",
            ComponentType = typeof(JournalPanel),
            Description = "Entries you logged, and how they settled.",
            DefaultWeight = 1.4,
            MinWidth = 460
        },
        new()
        {
            Key = Performance,
            Title = "Performance",
            Icon = Icons.Material.Filled.QueryStats,
            Group = "Analytics",
            ComponentType = typeof(PerformancePanel),
            Description = "ROI, bankroll, and closing-line value.",
            DefaultWeight = 1.1,
            MinWidth = 360
        },
        new()
        {
            Key = Ops,
            Title = "Ops",
            Icon = Icons.Material.Filled.MonitorHeart,
            Group = "Operations",
            ComponentType = typeof(OpsPanel),
            Description = "Source health, SLOs, and open alerts.",
            DefaultWeight = 1.3,
            MinWidth = 420
        },
        new()
        {
            Key = Incidents,
            Title = "Incidents",
            Icon = Icons.Material.Filled.ReportProblem,
            Group = "Operations",
            ComponentType = typeof(IncidentsPanel),
            Description = "Events and their root-cause analyses.",
            DefaultWeight = 1.2,
            MinWidth = 400
        },
        new()
        {
            Key = Runs,
            Title = "Runs",
            Icon = Icons.Material.Filled.History,
            Group = "Operations",
            ComponentType = typeof(RunsPanel),
            Description = "Every ingestion run, successful or not.",
            DefaultWeight = 1.3,
            MinWidth = 440
        },
        new()
        {
            Key = History,
            Title = "History",
            // Runs already owns the History glyph; this one reaches backwards rather than
            // listing what happened, and two windows wearing the same icon on one taskbar is
            // a chip you cannot identify at a glance.
            Icon = Icons.Material.Filled.Restore,
            Group = "Operations",
            ComponentType = typeof(HistoryPanel),
            Description = "Backfill past days from free sources, and what is already held.",
            DefaultWeight = 1.1,
            MinWidth = 380
        },
        new()
        {
            Key = Desk,
            Title = "Window manager",
            Icon = Icons.Material.Filled.Tune,
            Group = "System",
            ComponentType = typeof(WindowManagerPanel),
            Description = "Window limit, primary window, resolution, taskbar.",
            // A settings pane reads fine narrow.
            DefaultWeight = 0.8,
            MinWidth = 320
        },
        new()
        {
            Key = Parts,
            Title = "Parts bin",
            Icon = Icons.Material.Filled.Widgets,
            Group = "System",
            ComponentType = typeof(PartsPanel),
            Description = "Every reusable control, live and side by side.",
            // Opened next to the panel being built, so it needs a real column rather than
            // a strip — the controls have to be judged at the width they will ship at.
            DefaultWeight = 1.0,
            MinWidth = 380
        }
    ];

    public static WindowDefinition? Find(string key)
        => All.FirstOrDefault(d => d.Key == key);

    /// <summary>
    /// Named sets of windows, in row order. These are the "workflows" — a shape of desk for
    /// a task. Widths come from the horizontal layout, so a workspace works on any screen.
    /// </summary>
    public static readonly IReadOnlyList<Workspace> Workspaces =
    [
        new(
            "Morning triage",
            "Health first, then what broke, then the runs behind it.",
            [Ops, Incidents, Runs]),
        new(
            "Line watch",
            "The slate beside a movement chart.",
            [Dashboard, Odds]),
        new(
            "Review",
            "Settled entries against the numbers they produced.",
            [Journal, Performance])
    ];
}
