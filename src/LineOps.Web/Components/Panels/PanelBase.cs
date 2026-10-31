using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Windowing;

namespace LineOps.Web.Components.Panels;

/// <summary>
/// This app's panels: the Desk's window plumbing (<see cref="DeskPanelBase"/> — load, retry,
/// refresh, pulse and unsaved input) plus the sports helpers every LineOps window reads from —
/// a game's start, its matchup, its probables, where it stands, a price and a line.
/// </summary>
public abstract class PanelBase : DeskPanelBase
{
    /// <summary>
    /// When a game starts, as a date and time.
    ///
    /// Not the day of week. "Sat 14:30" only reads as a date for the two days either side of
    /// today, and a desk that now holds a full season of games is regularly looking at neither.
    /// A date is unambiguous at any distance, which is what a column of them needs to be.
    /// In the league's zone, labelled — see <see cref="DisplayTime"/>.
    /// </summary>
    protected static string Starts(DateTimeOffset at) => DisplayTime.Stamp(at);

    /// <summary>Just the day, for columns where the time is noise.</summary>
    protected static string Day(DateTimeOffset at) => DisplayTime.Day(at);

    /// <summary>
    /// A game named in full: "Colorado Rockies at San Francisco Giants".
    ///
    /// Abbreviations are fine in a dense grid where the column is three characters wide and the
    /// same teams repeat down it. They are not fine anywhere a game is being *identified* — a
    /// window title, a picker, the thing you just highlighted — because "CR at SFG" asks the
    /// reader to decode before they can confirm they picked the right game.
    /// </summary>
    protected static string Matchup(Game? game)
        => game is null
            ? "—"
            : $"{game.AwayTeam?.Name ?? "Away"} at {game.HomeTeam?.Name ?? "Home"}";

    /// <summary>
    /// "Senga v Sale" — the announced starters, away first like the matchup. A side MLB has not
    /// named reads TBD; null when neither is known, so nothing is drawn for a game without them.
    /// </summary>
    protected static string? Probables(Game game)
        => game.AwayProbablePitcher is null && game.HomeProbablePitcher is null
            ? null
            : $"{Surname(game.AwayProbablePitcher)} vs {Surname(game.HomeProbablePitcher)}";

    /// <summary>
    /// Everything after the first name, not the last word: the last word of "CJ Van Eyk" is
    /// "Eyk", and of "Vladimir Guerrero Jr." is "Jr.".
    /// </summary>
    protected static string Surname(string? fullName)
    {
        if (fullName is null)
            return "TBD";

        var space = fullName.Trim().IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName.Trim()[(space + 1)..];
    }

    /// <summary>A game named in full, with its start date — for pickers listing many games.</summary>
    protected static string MatchupWithDate(Game? game)
        => game is null ? "—" : $"{Matchup(game)} — {Day(game.StartsAt)}";

    /// <summary>
    /// What a game's window says after its title: where the game stands. The title is already
    /// the matchup, so repeating it here read as the same name twice. "Final · NYM 4–2 ATL"
    /// once there is a result, the start for a game not yet played.
    /// </summary>
    protected static string StateStatus(Game game)
        => Scoreline.Has(game) ? $"{Phase(game)} · {Scoreline.Format(game)}" : Phase(game);

    /// <summary>Where a game stands in a word, or when it starts if it has not.</summary>
    protected static string Phase(Game game) => game.Status switch
    {
        GameStatus.Live => "Live",
        GameStatus.Final => "Final",
        GameStatus.Postponed => "Postponed",
        _ => Starts(game.StartsAt)
    };

    /// <summary>
    /// What a team's window says after its title: the season's record, "5-0 · 2026". Counted
    /// over the whole season rather than the split on screen, so the bar holds still while a
    /// filter changes.
    /// </summary>
    protected static string StateStatus(IReadOnlyList<TeamGameLogRow> season, string? seasonLabel)
    {
        var record = GameLogSplits.FormatRecord(season);

        return seasonLabel is null ? record : $"{record} · {seasonLabel}";
    }

    protected static string Price(int american) => american > 0 ? $"+{american}" : american.ToString();

    /// <summary>
    /// A line as a book prints it: "-3", "+4.5". A line read back from the journal carries the
    /// column's scale, and decimal's own ToString printed it as "-3.00".
    /// </summary>
    protected static string Line(decimal value) => value.ToString("+0.##;-0.##;0");

    protected static string Age(double minutes) => minutes switch
    {
        < 1 => "just now",
        < 60 => $"{minutes:F0}m",
        < 1440 => $"{minutes / 60:F1}h",
        _ => $"{minutes / 1440:F1}d"
    };
}
