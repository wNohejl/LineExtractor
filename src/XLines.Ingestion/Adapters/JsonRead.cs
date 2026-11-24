using System.Globalization;
using System.Text.Json;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// The JSON readers the ESPN and MLB adapters share. Every one answers null (or the default
/// element) for a parent that is not an object, rather than throwing, so a section a provider left
/// out, or sent as null, reads as unstated all the way down and a chain of reads needs no guards.
///
/// <para>
/// Some readers are deliberately not here, because they look alike and are not. MLB's
/// <c>Integer</c> rejects "82.5" where ESPN's <c>WholeNumber</c> rounds it: MLB states a whole
/// temperature or nothing, and ESPN's weather and predictor write figures with a decimal that are
/// meant whole. The gamecast's <c>GamecastJson</c> reads a number where a string was asked for and
/// a missing flag as false, which is right for a play-by-play row and wrong for a context. The
/// injuries' <c>Field</c> also reads an id written as a number. Each stays beside the parse it
/// was written for.
/// </para>
/// </summary>
internal static class JsonRead
{
    /// <summary>A child object, or the default element where there is none, so a chain of reads stays safe.</summary>
    public static JsonElement Child(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(name, out var child)
           && child.ValueKind == JsonValueKind.Object
            ? child
            : default;

    /// <summary>A string property exactly as sent; null where it is absent or not a string.</summary>
    public static string? String(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>A string that says something, trimmed; null for a missing, blank or non-string field.</summary>
    public static string? Words(JsonElement parent, string name)
        => String(parent, name) is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>
    /// A whole number written as a number; null for a string, a fraction or a figure out of range.
    /// A provider's numeric string is a different question, answered by the adapter that sends one.
    /// </summary>
    public static int? Int(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;

    /// <summary>A true or false as sent; null where the flag is absent or not a boolean, never false by default.</summary>
    public static bool? Bool(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    /// <summary>
    /// A line as a book writes it — "-7.5", "+1.5", a total's "o8.5" or "u8.5" — with the over or
    /// under marker stripped before it is parsed.
    ///
    /// <para>
    /// A pick'em is a spread of zero written in words, "PK" or "EVEN", and is read as 0 only where
    /// the line is a spread. A total of nothing is not a total, so a total that says "EVEN" is no
    /// line. A price is never parsed here: "EVEN" as a price is +100, which is
    /// <c>EspnStatsAdapter.TryParseAmerican</c>'s answer, not this one's.
    /// </para>
    /// </summary>
    /// <param name="raw">The line as sent.</param>
    /// <param name="spread">True where the line is a handicap, the one kind a pick'em can be.</param>
    /// <param name="line">The line, or 0 where none was read.</param>
    public static bool TryParseLine(string? raw, bool spread, out decimal line)
    {
        line = 0;

        if (raw is null)
            return false;

        if (raw.Trim().Equals("PK", StringComparison.OrdinalIgnoreCase)
            || raw.Trim().Equals("EVEN", StringComparison.OrdinalIgnoreCase))
            return spread;

        return decimal.TryParse(raw.TrimStart('o', 'O', 'u', 'U'), NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out line);
    }
}
