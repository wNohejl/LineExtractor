using System.Text.Json;

namespace LineOps.Data.CrossReference;

/// <summary>One player's passing line from one game: what the quarterback reader needs of it.</summary>
/// <param name="SeasonYear">The game's season (<c>Game.SeasonYear</c>), where the provider stamped one.</param>
public sealed record PassingLine(
    int TeamId, int GameId, DateTimeOffset StartsAt, string Player, string StatLine, int? SeasonYear = null);

/// <summary>
/// The quarterback the Board names under a club, and why: the sentence its tooltip carries.
/// Both null for a club with no passing on record; <see cref="Name"/> alone null when the starter
/// is ruled out and nobody has thrown in his place.
/// </summary>
public sealed record StartingQuarterback(string? Name, string? Basis);

/// <summary>
/// Who starts.
///
/// <para>
/// Baseball names its starters: the MLB Stats API publishes probable pitchers, and the spine
/// writes them onto the game. Football does not, through any feed the desk reads, and ESPN's box
/// scores carry no positions. What they do carry is every passing line, so the quarterback a
/// club started its last game with is the passer who threw the most in it. That is the starter
/// until the club says otherwise, and the honest thing to show beside a matchup — labelled as a
/// last start, not as an announcement.
/// </para>
///
/// <para>
/// Until the club rules him out. The league's injury report is the club's own word, and a
/// quarterback it lists Out, Doubtful or on Injured Reserve does not start: the Vikings' last game
/// was Murray's, and on 9 November 2026 the club had ruled him out of the next. The starter is
/// then the passer who started a game this season without him — Wentz, in weeks one and two — and
/// the tooltip says so, injury and all. Where nobody has thrown in his place, the Board names no
/// one rather than the man who is out.
/// </para>
/// </summary>
public static class Starters
{
    /// <summary>The basis of a starter nobody has ruled out.</summary>
    public const string LastGameBasis = "Started at quarterback in the club's last game";

    /// <summary>The basis of a starter read from the game itself, once it is under way or over.</summary>
    public const string ThisGameBasis = "At quarterback in this game";

    /// <summary>The report's word for a player who will not play: Out, Doubtful, Injured Reserve.</summary>
    public static bool IsOut(string? status)
        => status is not null
           && (status.Equals("Out", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Doubtful", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Injured Reserve", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The passer with the most attempts in the club's most recent game before
    /// <paramref name="before"/>, or null when it has none on record. A trick-play pass by a
    /// receiver, or a backup's two series, never outranks the quarterback who played the game.
    /// Reads no injury report; see <see cref="Quarterback"/>.
    /// </summary>
    public static string? LastQuarterback(IEnumerable<PassingLine> lines, int teamId, DateTimeOffset before)
        => Quarterback(lines, new Dictionary<string, (string Status, string? Note)>(), teamId, before).Name;

    /// <summary>
    /// The club's quarterback for a game at <paramref name="before"/>: the last game's leading
    /// passer, unless <paramref name="injuriesByPlayer"/> (by full name) has him out. Then the
    /// leading passer of the club's most recent game this season that he did not throw in, or
    /// failing that its most recent other passer — either way one the report does not have out
    /// too. "This season" is the last game's: a week-one game reads the season before it.
    /// </summary>
    public static StartingQuarterback Quarterback(
        IEnumerable<PassingLine> lines,
        IReadOnlyDictionary<string, (string Status, string? Note)> injuriesByPlayer,
        int teamId,
        DateTimeOffset before,
        int? gameId = null)
    {
        // A game under way or over has its own box score, and who is throwing in it is not an
        // inference: on 9 November the Vikings' chip read Murray, from the last game before
        // tonight's, while Wentz was 7 of 19 in tonight's. The game's own passer comes first.
        if (gameId is { } id
            && Leader(lines.Where(l => l.TeamId == teamId && l.GameId == id)) is { } playing)
            return new StartingQuarterback(playing, ThisGameBasis);

        var games = lines
            .Where(l => l.TeamId == teamId && l.StartsAt < before)
            .GroupBy(l => (l.GameId, l.StartsAt))
            .OrderByDescending(g => g.Key.StartsAt)
            .Select(g => (g.Key.StartsAt, Season: g.First().SeasonYear, Lines: g.ToList()))
            .ToList();

        if (games.Count == 0 || Leader(games[0].Lines) is not { } last)
            return new StartingQuarterback(null, null);

        var report = new Dictionary<string, (string Status, string? Note)>(injuriesByPlayer, StringComparer.OrdinalIgnoreCase);

        if (!report.TryGetValue(last, out var injury) || !IsOut(injury.Status))
            return new StartingQuarterback(last, LastGameBasis);

        bool Available(string player) => !player.Equals(last, StringComparison.OrdinalIgnoreCase)
                                          && !(report.TryGetValue(player, out var i) && IsOut(i.Status));

        var season = games[0].Season;
        var thisSeason = games.Skip(1).Where(g => season is null || g.Season == season).ToList();

        var without = thisSeason
            .Where(g => !g.Lines.Any(l => l.Player.Equals(last, StringComparison.OrdinalIgnoreCase)))
            .Select(g => Leader(g.Lines))
            .FirstOrDefault(p => p is not null && Available(p));

        var opening = $"{last} {Phrase(injury.Status)}; ";
        var note = string.IsNullOrWhiteSpace(injury.Note) ? "" : $" {injury.Note.Trim()}";

        if (without is not null)
            return new StartingQuarterback(without, $"{opening}{without} started the last game the club played without him.{note}");

        var relief = games
            .Where(g => season is null || g.Season == season)
            .SelectMany(g => g.Lines.OrderByDescending(l => Attempts(l.StatLine)))
            .Where(l => Available(l.Player) && Attempts(l.StatLine) > 0)
            .Select(l => l.Player)
            .FirstOrDefault();

        return relief is not null
            ? new StartingQuarterback(relief, $"{opening}{relief} is the club's most recent other passer.{note}")
            : new StartingQuarterback(null, $"{opening}no replacement has thrown for the club this season.{note}");
    }

    /// <summary>The passer with the most attempts in one game's lines, or null when nobody threw.</summary>
    private static string? Leader(IEnumerable<PassingLine> game)
        => game
            .Select(l => (l.Player, Attempts: Attempts(l.StatLine)))
            .Where(x => x.Attempts > 0)
            .OrderByDescending(x => x.Attempts)
            .Select(x => x.Player)
            .FirstOrDefault();

    private static string Phrase(string status)
        => status.Equals("Injured Reserve", StringComparison.OrdinalIgnoreCase) ? "is on injured reserve"
            : status.Equals("Doubtful", StringComparison.OrdinalIgnoreCase) ? "is doubtful"
            : "is out";

    /// <summary>Pass attempts in an ESPN line's <c>C/ATT</c> — "20/28" is 28 — or 0 when it has none.</summary>
    public static int Attempts(string statLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(statLine);

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("C/ATT", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } text
                && text.IndexOf('/') is var slash and >= 0
                && int.TryParse(text[(slash + 1)..], out var attempts))
            {
                return attempts;
            }
        }
        catch (JsonException)
        {
            // An unreadable line is a line with no passing in it, not a board that fails to load.
        }

        return 0;
    }
}
