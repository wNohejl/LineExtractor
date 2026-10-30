using LineOps.Core.Analytics;
using LineOps.Core.Entities;

using LineOps.Desk.Windowing;

namespace LineOps.Web.Windowing;

/// <summary>
/// The routes between windows, in one place.
///
/// A grid cell naming a game, a player or a source is a reference to data that has its own
/// window, and following it should be one click rather than a trip through the launcher. Each
/// panel could resolve its own catalog entry and call <see cref="WindowManager.Open"/>, and
/// several already did — which is how the slate ended up titling a window "CR @ SFG" while
/// everywhere else spelled the teams out.
///
/// Keeping the routes here means a destination's title, parameters and singleton behaviour are
/// decided once, by the thing that owns them, rather than at each call site.
///
/// <para>
/// The subject routes take <c>from</c>, the window the link sits in. A subject window passes its
/// own id and the destination opens in it, with Back to return (<see cref="WindowManager.Follow"/>);
/// anything else passes nothing and gets a window of its own.
/// </para>
/// </summary>
public static class WindowShortcuts
{
    /// <summary>
    /// The consolidated game view — lines, team data and players for one game. Not a singleton:
    /// comparing two games side by side is the point.
    /// </summary>
    public static void OpenGame(this WindowManager manager, Game game, string? from = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Game) is not { } definition)
            return;

        manager.Follow(
            from,
            definition,
            new Dictionary<string, object> { ["GameId"] = game.Id },
            Describe(game));
    }

    /// <summary>Line movement for one game, opened on its own when the trend is what's wanted.</summary>
    public static void OpenMovement(this WindowManager manager, int gameId, string? name = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Odds) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["GameId"] = gameId },
            name);
    }

    /// <summary>
    /// One player's own view — their game log and averages.
    ///
    /// <para>
    /// This used to open the Players <i>list</i> focused on the player, which answers a
    /// different question: a roster view says who is on a team, not how a player has been
    /// going. Following a name should land on the thing the name refers to.
    /// </para>
    /// </summary>
    public static void OpenPlayer(this WindowManager manager, int playerId, string? name = null, string? from = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Player) is not { } definition)
            return;

        manager.Follow(
            from,
            definition,
            new Dictionary<string, object> { ["PlayerId"] = playerId },
            name);
    }

    /// <summary>The Team window, focused on one team's recent record and roster.</summary>
    public static void OpenTeam(this WindowManager manager, int teamId, string? name = null, string? from = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Team) is not { } definition)
            return;

        manager.Follow(
            from,
            definition,
            new Dictionary<string, object> { ["TeamId"] = teamId },
            name);
    }

    /// <summary>
    /// Every previous meeting between the two sides of a game. Takes the game rather than the
    /// teams, because the matchup is what a row on the desk names.
    /// </summary>
    public static void OpenHeadToHead(this WindowManager manager, int gameId, string? name = null, string? from = null)
    {
        if (manager.Catalog.Find(WindowCatalog.HeadToHead) is not { } definition)
            return;

        manager.Follow(
            from,
            definition,
            new Dictionary<string, object> { ["GameId"] = gameId },
            name is null ? null : $"H2H · {name}");
    }

    /// <summary>Ingestion runs, narrowed to one source.</summary>
    public static void OpenRuns(this WindowManager manager, int sourceId, string? sourceName = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Runs) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["SourceId"] = sourceId },
            sourceName is null ? null : $"Runs · {sourceName}");
    }

    /// <summary>
    /// Operations — source health, the open alerts, and the drill.
    ///
    /// <para>
    /// Takes no subject: alerts are about the desk rather than about a game, so there is
    /// nothing to narrow it to. It exists so a panel counting alerts can hand the reader
    /// the window that lists them instead of leaving them to find it on the rail.
    /// </para>
    /// </summary>
    public static void OpenOps(this WindowManager manager)
    {
        if (manager.Catalog.Find(WindowCatalog.Ops) is not { } definition)
            return;

        manager.Open(definition);
    }

    /// <summary>
    /// The history backfill.
    ///
    /// <para>
    /// Takes no subject, for the same reason <see cref="OpenOps"/> does not: the walk is about
    /// the desk's own record rather than about any one game. It exists because three panels
    /// tell the reader in prose that history is gathered elsewhere, and a sentence naming a
    /// window is worse than a key that opens it.
    /// </para>
    /// </summary>
    public static void OpenHistory(this WindowManager manager)
    {
        if (manager.Catalog.Find(WindowCatalog.History) is not { } definition)
            return;

        manager.Open(definition);
    }

    /// <summary>
    /// The journal. Takes no subject — it is the ledger, not one entry in it — and exists so a
    /// panel with nothing settled to report can hand over the window entries are logged in.
    /// </summary>
    public static void OpenJournal(this WindowManager manager)
    {
        if (manager.Catalog.Find(WindowCatalog.Journal) is not { } definition)
            return;

        manager.Open(definition);
    }

    /// <summary>
    /// A new wager window on a bet already made — the Journal's Duplicate, and the wager
    /// window's own. Never re-aims an open window: the copy is a second bet, and the window it
    /// was copied from keeps what it was showing. The title is the matchup where there is one.
    /// </summary>
    public static void OpenWagerCopy(this WindowManager manager, Services.WagerPrefill prefill, string? title = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Wager) is not { } definition)
            return;

        manager.Open(definition, prefill.ToParameters(), title);
    }

    /// <summary>
    /// The wager window preloaded from a game: the game's form, and the side and book when the
    /// door names them (a price pressed in the Game window), else step 1 with every side offered.
    /// The Board's row and its prices open the same window through their follow-up launcher.
    /// </summary>
    public static void OpenWager(this WindowManager manager, Game game,
        string? market = null, string? outcome = null, string? book = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Wager) is not { } definition)
            return;

        var parameters = new Dictionary<string, object> { ["GameId"] = game.Id };
        if (market is not null && outcome is not null)
        {
            parameters["Market"] = market;
            parameters["Outcome"] = outcome;
        }
        if (book is not null)
            parameters["Book"] = book;

        manager.Open(definition, parameters, Describe(game));
    }

    /// <summary>Incidents, opened on one incident.</summary>
    public static void OpenIncident(this WindowManager manager, int incidentId, string? title = null)
    {
        if (manager.Catalog.Find(WindowCatalog.Incidents) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["IncidentId"] = incidentId },
            title);
    }

    /// <summary>
    /// A game named in full. Window chips are narrow, but an abbreviation in a title is a
    /// puzzle rather than a label — the chip truncates gracefully, a decoded name does not.
    /// </summary>
    public static string Describe(Game game)
        => FightSports.IsFight(game.Sport?.Key)
            // A fight is billed red corner first, and nobody is at home (ADR 0021).
            ? $"{game.HomeTeam?.Name ?? "Red"} vs {game.AwayTeam?.Name ?? "Blue"}"
            : $"{game.AwayTeam?.Name ?? "Away"} at {game.HomeTeam?.Name ?? "Home"}";
}
