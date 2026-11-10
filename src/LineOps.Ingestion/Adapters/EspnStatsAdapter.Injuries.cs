using System.Text.Json;
using System.Text.RegularExpressions;
using LineOps.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Adapters;

public partial class EspnStatsAdapter : IInjurySource
{
    /// <summary>
    /// The leagues whose injury report is read. The NFL only, for now: it is the league whose
    /// starter the desk infers rather than is told — MLB names its probable pitchers, and a fight's
    /// card is its fighters — so it is the one where a club ruling its quarterback out changes
    /// what the Board should say.
    ///
    /// <para>
    /// The one list of them: the slate's injury sync reads this set rather than keeping its own.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> InjuryLeagues =
        new HashSet<string>(["nfl"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A league's injury report, every club in one request: <c>{league}/injuries</c>.
    ///
    /// <para>
    /// Read rather than the depth chart, which still listed Murray as the Vikings' first
    /// quarterback on 9 November 2026 two days after the club ruled him out of Monday's game. The
    /// report is the club's own word, relayed: who is Out, Doubtful, Questionable, on Injured
    /// Reserve — and who is Active again, which is how a backup who has not thrown since week two
    /// is known to be the starter.
    /// </para>
    ///
    /// <para>
    /// ESPN lists exactly twenty-five entries a club, newest first, and <c>?limit=</c> changes
    /// nothing. A player ruled out weeks ago falls off the bottom as newer entries push him out,
    /// so a player missing from the report has not been cleared by it: the service leaves him as
    /// he was, and a status ages out after <see cref="LineOps.Core.Entities.Player.InjuryStatusLife"/>.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<CanonicalInjury>> FetchInjuriesAsync(string sportKey, CancellationToken ct)
    {
        if (!InjuryLeagues.Contains(sportKey) || MapPath(sportKey) is not { } path)
            return [];

        using var doc = await GetJsonAsync($"{path}/injuries", ct);
        var report = ParseInjuries(doc.RootElement).ToList();

        logger.LogInformation("{Source}: {Sport} injury report -> {Count} players", SourceKey, sportKey, report.Count);

        return report;
    }

    /// <summary>
    /// The report's entries, club by club. An athlete on this endpoint carries no <c>id</c>; his
    /// ESPN id is the one in his player-card link (<c>/player/_/id/3917315/kyler-murray</c>), and
    /// it is read from there when the field is absent. An entry with neither is skipped: a name
    /// alone is not enough to say whose status it is.
    /// </summary>
    internal static IEnumerable<CanonicalInjury> ParseInjuries(JsonElement root)
    {
        if (!root.TryGetProperty("injuries", out var clubs) || clubs.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var club in clubs.EnumerateArray())
        {
            if (!club.TryGetProperty("injuries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                continue;

            var clubId = Field(club, "id");
            var clubName = Field(club, "displayName");

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("athlete", out var athlete) || athlete.ValueKind != JsonValueKind.Object)
                    continue;

                var id = AthleteId(athlete);
                var name = Field(athlete, "displayName");
                var status = Field(entry, "status");

                if (id is null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(status))
                    continue;

                var team = athlete.TryGetProperty("team", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
                var teamId = team.ValueKind == JsonValueKind.Object ? Field(team, "id") ?? clubId : clubId;
                var teamName = team.ValueKind == JsonValueKind.Object ? Field(team, "displayName") ?? clubName : clubName;

                var position = athlete.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.Object
                    ? Field(p, "abbreviation")
                    : null;

                yield return new CanonicalInjury(
                    id, name, position, teamId, teamName ?? string.Empty, status, InjuryNote(Field(entry, "shortComment")));
            }
        }
    }

    /// <summary>
    /// The report's note as the desk shows it: its first sentence, and nothing for a note that
    /// only restates the status — ESPN writes "ir" on every injured-reserve entry.
    /// </summary>
    internal static string? InjuryNote(string? comment)
    {
        var text = comment?.Trim();

        if (string.IsNullOrEmpty(text) || text.Length < 4)
            return null;

        foreach (Match end in SentenceEnd().Matches(text))
        {
            var before = text[..end.Index];
            var word = before[(before.LastIndexOf(' ') + 1)..];

            // "Jr. Active" and "vs. Bills" end a word, not a sentence.
            if (!Abbreviations.Contains(word))
                return text[..(end.Index + 1)];
        }

        return text;
    }

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "vs", "Jr", "Sr", "St", "Mr", "Dr", "No", "Mt", "Ft"
    };

    /// <summary>A full stop, question or exclamation mark, then a space and a capital.</summary>
    [GeneratedRegex(@"[.!?](?=\s+[A-Z])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"/id/(\d+)(?:/|$)")]
    private static partial Regex PlayerCardId();

    private static string? AthleteId(JsonElement athlete)
    {
        if (Field(athlete, "id") is { Length: > 0 } id)
            return id;

        if (!athlete.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var link in links.EnumerateArray())
        {
            if (Field(link, "href") is { } href && PlayerCardId().Match(href) is { Success: true } match)
                return match.Groups[1].Value;
        }

        return null;
    }

    private static string? Field(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;
}
