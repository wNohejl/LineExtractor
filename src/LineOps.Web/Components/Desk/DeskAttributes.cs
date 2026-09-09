namespace LineOps.Web.Components.Desk;

/// <summary>
/// Splatting guard.
///
/// A component that builds its own class string cannot also let <c>@attributes</c> carry a
/// <c>class</c> through: Blazor applies the splat last, so one stray attribute at a call site
/// silently erases every modifier the component computed and the tile loses its meaning
/// without anything failing. The desk's rule is that a component owns its own class and
/// style, and call sites reach them through explicit <c>Class</c> / <c>Style</c> parameters
/// that get merged rather than substituted.
/// </summary>
internal static class DeskAttributes
{
    /// <summary>
    /// Returns <paramref name="attributes"/> with any <c>class</c> or <c>style</c> entry removed,
    /// or the same instance when there is nothing to strip.
    /// </summary>
    public static IReadOnlyDictionary<string, object>? WithoutClassOrStyle(
        IReadOnlyDictionary<string, object>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return attributes;
        }

        var carriesOwned = attributes.Keys.Any(IsOwned);
        if (!carriesOwned)
        {
            return attributes;
        }

        return attributes
            .Where(pair => !IsOwned(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>
    /// The component's own declarations with the caller's appended, so a splatted
    /// <c>style</c> still wins on any property it actually names but cannot silently
    /// replace the whole attribute. Mirrors <c>DeskSwitch</c>.
    /// </summary>
    public static string? MergeStyle(string? own, IReadOnlyDictionary<string, object>? attributes)
    {
        var caller = attributes is not null
                     && attributes.TryGetValue("style", out var value)
            ? value?.ToString()
            : null;

        if (own is not { Length: > 0 })
        {
            return caller is { Length: > 0 } ? caller : null;
        }

        return caller is { Length: > 0 } ? $"{own}; {caller}" : own;
    }

    private static bool IsOwned(string name) =>
        string.Equals(name, "class", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "style", StringComparison.OrdinalIgnoreCase);
}
