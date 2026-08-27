using LineOps.Core.Entities;

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
/// </summary>
public static class WindowShortcuts
{
    /// <summary>
    /// The consolidated game view — lines, team data and players for one game. Not a singleton:
    /// comparing two games side by side is the point.
    /// </summary>
    public static void OpenGame(this WindowManager manager, Game game)
    {
        if (WindowCatalog.Find(WindowCatalog.Game) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["GameId"] = game.Id },
            Describe(game));
    }

    /// <summary>Line movement for one game, opened on its own when the trend is what's wanted.</summary>
    public static void OpenMovement(this WindowManager manager, int gameId, string? name = null)
    {
        if (WindowCatalog.Find(WindowCatalog.Odds) is not { } definition)
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
    public static void OpenPlayer(this WindowManager manager, int playerId, string? name = null)
    {
        if (WindowCatalog.Find(WindowCatalog.Player) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["PlayerId"] = playerId },
            name);
    }

    /// <summary>The Team window, focused on one team's recent record and roster.</summary>
    public static void OpenTeam(this WindowManager manager, int teamId, string? name = null)
    {
        if (WindowCatalog.Find(WindowCatalog.Team) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["TeamId"] = teamId },
            name);
    }

    /// <summary>
    /// Every previous meeting between the two sides of a game. Takes the game rather than the
    /// teams, because the matchup is what a row on the desk names.
    /// </summary>
    public static void OpenHeadToHead(this WindowManager manager, int gameId, string? name = null)
    {
        if (WindowCatalog.Find(WindowCatalog.HeadToHead) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["GameId"] = gameId },
            name is null ? null : $"H2H · {name}");
    }

    /// <summary>Ingestion runs, narrowed to one source.</summary>
    public static void OpenRuns(this WindowManager manager, int sourceId, string? sourceName = null)
    {
        if (WindowCatalog.Find(WindowCatalog.Runs) is not { } definition)
            return;

        manager.Open(
            definition,
            new Dictionary<string, object> { ["SourceId"] = sourceId },
            sourceName is null ? null : $"Runs · {sourceName}");
    }

    /// <summary>Incidents, opened on one incident.</summary>
    public static void OpenIncident(this WindowManager manager, int incidentId, string? title = null)
    {
        if (WindowCatalog.Find(WindowCatalog.Incidents) is not { } definition)
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
        => $"{game.AwayTeam?.Name ?? "Away"} at {game.HomeTeam?.Name ?? "Home"}";
}
