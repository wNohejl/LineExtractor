using LineOps.Core.Entities;
using LineOps.Data.CrossReference;

namespace LineOps.Web.Components;

/// <summary>
/// One finished-or-not result, reduced to what a form run needs to draw it.
///
/// <para>
/// Two lookups produce recent games — <see cref="TeamGameLogRow"/> from the game log and
/// <see cref="TeamRecentGame"/> from the matchup cross-reference — and they agree on the seven
/// fields that matter here while differing on everything else. Rather than teach the run about
/// both, or make one lookup depend on the other's row type, both narrow to this.
/// </para>
///
/// <para>
/// It sits beside the views rather than in <c>Desk/</c> because it knows what a game is.
/// </para>
/// </summary>
/// <param name="StartsAt">When the game was played, which is what puts the run in order.</param>
/// <param name="Opponent">Who it was against, for the tag's title.</param>
/// <param name="Home">Whether it was at home — "vs" or "at" in the title.</param>
/// <param name="Won">
/// Null while the game is unfinished. Deliberately nullable rather than defaulted to a loss:
/// a game that has not ended has not been lost.
/// </param>
/// <param name="ScoreFor">Points scored, when the score means anything.</param>
/// <param name="ScoreAgainst">Points conceded, on the same footing.</param>
public readonly record struct FormResult(
    DateTimeOffset StartsAt,
    string Opponent,
    bool Home,
    bool? Won,
    int? ScoreFor,
    int? ScoreAgainst)
{
    /// <summary>Whether the score is worth printing. A scheduled game has no score, not 0-0.</summary>
    public bool HasScore => ScoreFor is not null && ScoreAgainst is not null;

    public static FormResult From(TeamGameLogRow r)
        => new(r.StartsAt, r.Opponent, r.Home, r.Won, r.ScoreFor, r.ScoreAgainst);

    public static FormResult From(TeamRecentGame r)
        => new(r.StartsAt, r.Opponent, r.Home, r.Won, r.ScoreFor, r.ScoreAgainst);

    public static IReadOnlyList<FormResult> From(IEnumerable<TeamGameLogRow> rows)
        => rows.Select(From).ToList();

    public static IReadOnlyList<FormResult> From(IEnumerable<TeamRecentGame> rows)
        => rows.Select(From).ToList();
}
