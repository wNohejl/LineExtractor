using LineOps.Web.Theming;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// The splat plumbing shared by <c>DeskRow</c> and <c>DeskStack</c>.
///
/// <para>
/// Both compose a <c>class</c> and a <c>style</c> of their own, and both capture
/// unmatched attributes. Blazor applies a splatted attribute after the element's own,
/// so a caller writing <c>style="margin:0"</c> on a component that sets
/// <c>--desk-gap</c> replaces the gap rather than adding a margin — the failure the
/// gate/switch post-mortem is about. The rule here is the one DeskSwitch settled on:
/// hold <c>class</c> and <c>style</c> out of the splat, and fold the caller's values
/// into the component's own so they are appended rather than substituted.
/// </para>
///
/// <para>
/// DeskSwitch inlines this because it is the only component that needed it. Two more
/// wanting the identical code is the point at which copying it a third time stops
/// being cheaper than naming it.
/// </para>
/// </summary>
internal static class DeskLayout
{
    /// <summary>
    /// The component's own gap declaration with the caller's style appended. Appending
    /// means the caller still wins any property they actually name, while the gap
    /// survives the properties they did not.
    /// </summary>
    public static string? MergeStyle(DeskSpace? gap, IReadOnlyDictionary<string, object>? extra)
    {
        var own = gap is { } step ? $"--desk-gap:{DeskTheme.SpaceVar(step)}" : null;
        var caller = Lookup(extra, "style");

        return (own, caller) switch
        {
            (null, null) => null,
            (null, var c) => c,
            (var o, null) => o,
            var (o, c) => $"{o}; {c}"
        };
    }

    /// <summary>A splatted class, to be appended after the component's own classes.</summary>
    public static string? ExtraClass(IReadOnlyDictionary<string, object>? extra) => Lookup(extra, "class");

    /// <summary>Everything splattable except the two attributes the component composes itself.</summary>
    public static IReadOnlyDictionary<string, object>? WithoutClassAndStyle(
        IReadOnlyDictionary<string, object>? extra)
    {
        if (extra is null)
            return null;

        var rest = new Dictionary<string, object>(extra.Count);

        foreach (var (key, value) in extra)
        {
            if (!string.Equals(key, "style", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "class", StringComparison.OrdinalIgnoreCase))
            {
                rest[key] = value;
            }
        }

        return rest;
    }

    private static string? Lookup(IReadOnlyDictionary<string, object>? extra, string key) =>
        extra is not null
        && extra.TryGetValue(key, out var value)
        && value?.ToString() is { Length: > 0 } text
            ? text
            : null;
}
