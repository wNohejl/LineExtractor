using LineOps.Core.Entities;

namespace LineOps.Core.Analytics;

/// <summary>
/// How a game's score is written on a dense grid.
///
/// <para>
/// The score reached the desk already — <see cref="Game.HomeScore"/> and
/// <see cref="Game.AwayScore"/> are filled by the ESPN results pass (ADR 0011) — and the board
/// still printed a dash for every finished game, because nothing asked for it. A grid that
/// says "—" where a result exists is not sparse, it is wrong: the reader cannot tell a
/// scoreless fixture from a column that was never wired up.
/// </para>
///
/// <para>
/// The order matches how a game is named everywhere else on this desk: away first, because a
/// matchup reads "away at home". Reversing it here to lead with the home side would make the
/// score column disagree with the matchup column beside it.
/// </para>
/// </summary>
public static class Scoreline
{
    /// <summary>
    /// Whether this game has a result worth showing.
    ///
    /// <para>
    /// Two conditions, and the second is the one that bites. ESPN's scoreboard writes zeros for
    /// a fixture that has not started, so "either side has a score" is true of every game on
    /// tomorrow's slate — and a board that reads <c>ARI 0–0 SF</c> beside a SCHED tag is
    /// asserting a result nobody played. A score belongs to a game that is under way or
    /// finished; before that the column has nothing to say and should say nothing.
    /// </para>
    ///
    /// <para>
    /// A genuine scoreless live game is still reported, because it is genuinely 0–0.
    /// </para>
    /// </summary>
    public static bool Has(Game game)
        => game.Status is GameStatus.Live or GameStatus.Final
           && (game.HomeScore is not null || game.AwayScore is not null);

    /// <summary>
    /// The result as one string: <c>NYY 3–5 BOS</c>. An en dash, not a hyphen — the hyphen is
    /// already doing work in a handicap and the two columns sit next to each other.
    /// </summary>
    public static string Format(Game game)
        => Has(game)
            ? $"{Abbrev(game.AwayTeam, "AWY")} {game.AwayScore ?? 0}–{game.HomeScore ?? 0} {Abbrev(game.HomeTeam, "HOM")}"
            : "—";

    /// <summary>
    /// Which side is ahead, for a value that carries its own state rather than needing a legend.
    /// Null while there is nothing to compare.
    /// </summary>
    public static ScoreLeader Leader(Game game)
    {
        if (!Has(game) || game.HomeScore is not { } home || game.AwayScore is not { } away)
            return ScoreLeader.None;

        return home == away ? ScoreLeader.Level
            : home > away ? ScoreLeader.Home
            : ScoreLeader.Away;
    }

    /// <summary>
    /// A team's short mark. The provider's own abbreviation where there is one — ADR 0011 made
    /// that come through the pipeline rather than being invented at the point of display — and
    /// only a derived one where the source offered none.
    /// </summary>
    public static string Abbrev(Team? team, string fallback)
    {
        if (team is null)
            return fallback;

        if (!string.IsNullOrWhiteSpace(team.Abbrev))
            return team.Abbrev.ToUpperInvariant();

        var name = team.Name.Trim();

        return name.Length == 0
            ? fallback
            : name[..Math.Min(3, name.Length)].ToUpperInvariant();
    }
}

public enum ScoreLeader
{
    /// <summary>No score on record yet.</summary>
    None,
    Home,
    Away,
    Level
}

/// <summary>
/// Where a price on screen came from, said in the reader's terms.
///
/// <para>
/// ADR 0010 split odds into two tiers: the scan tier holds the live market while a game is
/// still ahead of us and is deleted once it starts, and one closing line per book survives
/// permanently. The board only ever read the scan tier, so every game that had started went
/// blank — sixteen rows of dashes on a nineteen-game slate — and the row explained itself with
/// "pre-match prices are dropped once a game starts", which described the storage policy
/// rather than answering the question.
/// </para>
///
/// <para>
/// The close is still the most useful number about a game in play: it is what the market
/// concluded, and it is what closing-line value is measured against. So it stays on the board,
/// muted rather than hidden, and says what it is on hover.
/// </para>
/// </summary>
public static class PriceProvenance
{
    /// <summary>
    /// What a closing price is, and how long before first pitch it was taken.
    ///
    /// The lead time matters: a book that stopped moving three hours out has a close that is
    /// three hours old, and that gap is the difference between "the market's last word" and
    /// "the last time anyone looked".
    /// </summary>
    /// <param name="capturedAt">When the observation that became the close was taken.</param>
    /// <param name="startsAt">First pitch, where the caller knows it.</param>
    /// <remarks>
    /// The line break is part of the answer, not formatting that leaked in. A tooltip carrying
    /// this has no width ceiling to wrap against — Mud writes <c>max-width: none</c> inline at
    /// runtime, which no stylesheet can beat without an <c>!important</c> the design system
    /// forbids — so the sentence arrives already broken where a reader would break it.
    /// </remarks>
    public static string Explain(DateTimeOffset capturedAt, DateTimeOffset? startsAt)
    {
        const string what = "Pre-match closing price —\nthe last number on the board before this game started.";

        if (startsAt is not { } start)
            return what;

        var lead = start - capturedAt;

        return lead <= TimeSpan.FromMinutes(1)
            ? $"{what}\nCaptured at the start."
            : $"{what}\nCaptured {Lead(lead)} before start.";
    }

    /// <summary>A lead time at the precision a reader can act on, never more.</summary>
    public static string Lead(TimeSpan lead)
    {
        var minutes = lead.TotalMinutes;

        return minutes switch
        {
            < 1 => "under a minute",
            < 60 => $"{minutes:F0}m",
            < 1440 => $"{minutes / 60:F1}h",
            _ => $"{minutes / 1440:F1}d"
        };
    }
}
