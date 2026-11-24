namespace XLines.Ingestion;

/// <summary>
/// Provider text cut to the column that stores it.
///
/// <para>
/// Providers document few of their string lengths, and one value longer than its column fails the
/// save that carries the whole slate, so every writer cuts to the column first. The cut never lands
/// inside a surrogate pair: a plain <c>value[..max]</c> through an emoji or a supplementary
/// character leaves a lone high surrogate, which is not valid UTF-16, and Npgsql's strict UTF-8
/// encoder then refuses the string and with it the whole save. Such a cut gives up one character
/// of the limit instead.
/// </para>
///
/// <para>
/// It sits in the root namespace so the services and the adapters share one copy rather than each
/// keeping a <c>Truncate</c> of its own.
/// </para>
/// </summary>
internal static class ColumnText
{
    /// <summary>
    /// <paramref name="value"/> as it fits in <paramref name="length"/> characters: unchanged when
    /// it already fits (null included), otherwise cut, one short where the cut would split a pair.
    /// </summary>
    public static string? Fit(string? value, int length)
        => value is { Length: var n } && n > length
            ? value[..(length > 0 && char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)]
            : value;
}
