using System.Text.Json;

namespace LineOps.Data.CrossReference;

/// <summary>One player's passing line from one game: what the quarterback reader needs of it.</summary>
public sealed record PassingLine(int TeamId, int GameId, DateTimeOffset StartsAt, string Player, string StatLine);

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
/// </summary>
public static class Starters
{
    /// <summary>
    /// The passer with the most attempts in the club's most recent game before
    /// <paramref name="before"/>, or null when it has none on record. A trick-play pass by a
    /// receiver, or a backup's two series, never outranks the quarterback who played the game.
    /// </summary>
    public static string? LastQuarterback(IEnumerable<PassingLine> lines, int teamId, DateTimeOffset before)
    {
        var lastGame = lines
            .Where(l => l.TeamId == teamId && l.StartsAt < before)
            .GroupBy(l => (l.GameId, l.StartsAt))
            .OrderByDescending(g => g.Key.StartsAt)
            .FirstOrDefault();

        return lastGame?
            .Select(l => (l.Player, Attempts: Attempts(l.StatLine)))
            .Where(x => x.Attempts > 0)
            .OrderByDescending(x => x.Attempts)
            .Select(x => x.Player)
            .FirstOrDefault();
    }

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
