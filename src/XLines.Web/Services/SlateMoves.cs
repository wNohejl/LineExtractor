using XLines.Core.Entities;
using XLines.Web.Components.Panels;

namespace XLines.Web.Services;

/// <summary>One book's move on one side of one game: where it opened, where it is now.</summary>
public sealed record SlateMove(Game Game, string Market, OddsPanel.MovementRow Row);

/// <summary>
/// What Line movement shows in Line watch before anything is pressed (ADR 0029): the slate's
/// biggest moves today, and the next games by start time for a slate where nothing has moved.
/// </summary>
public sealed record SlateOverview(IReadOnlyList<SlateMove> Moves, IReadOnlyList<Game> Next)
{
    public static readonly SlateOverview Empty = new([], []);
}

/// <summary>
/// The arithmetic of the overview, free of the database so the reading is tested on its own: the
/// same rows and the same measure of a move as Line movement's own table (OddsPanel).
/// </summary>
public static class SlateMoves
{
    /// <summary>How many moves the overview lists.</summary>
    public const int Take = 8;

    /// <summary>
    /// The biggest moves made today: every book's row on every side of every game in the slate,
    /// kept where the book's number now differs from where it opened and the change landed today
    /// (prices are written only when they change, so a row's last reading is when it last moved).
    /// A line move outranks any price move, since it changes what the bet is; then the cents.
    /// </summary>
    public static IReadOnlyList<SlateMove> Biggest(IEnumerable<OddsSnapshot> snapshots, IReadOnlyList<Game> games,
        DateTimeOffset since, int take = Take)
    {
        var byId = games.ToDictionary(g => g.Id);

        return snapshots
            .Where(s => byId.ContainsKey(s.GameId))
            .GroupBy(s => (s.GameId, s.Market, s.Outcome))
            .SelectMany(side => OddsPanel.MovementRows(side, side.Key.Outcome)
                .Select(row => new SlateMove(byId[side.Key.GameId], side.Key.Market, row)))
            .Where(m => m.Row.Moved && m.Row.Now.CapturedAt >= since)
            .OrderByDescending(m => Size(m.Row))
            .ThenByDescending(m => Leads(m))
            .ThenByDescending(m => m.Row.Now.CapturedAt)
            // One move a book makes on a spread or a total moves both of its sides (o47.5 to o48.5
            // is u47.5 to u48.5): it is said once, by the bigger side, the away side or the over
            // where they are the same size.
            .DistinctBy(m => (m.Game.Id, m.Market, m.Row.Book))
            .Take(take)
            .ToList();
    }

    /// <summary>The side a market is read by: the away club, or the over.</summary>
    private static bool Leads(SlateMove move)
        => string.Equals(move.Row.Now.Outcome, "over", StringComparison.OrdinalIgnoreCase)
           || string.Equals(move.Row.Now.Outcome, move.Game.AwayTeam?.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>A move's size, as Line movement's Move column sorts it: points first, then cents.</summary>
    public static double Size(OddsPanel.MovementRow row)
        => (double)Math.Abs(row.LineChange ?? 0m) * 10_000 + Math.Abs(row.PriceCents ?? 0);
}
