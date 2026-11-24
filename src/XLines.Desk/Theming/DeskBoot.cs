using System.Text.Json;

namespace XLines.Desk.Theming;

/// <summary>
/// The tables theme-boot.js needs to stamp the stored desk before the first paint, built once from
/// the catalogue and the looks and written by DeskHead into the page as a JSON data block
/// (<c>&lt;script type="application/json" id="desk-boot"&gt;</c>) ahead of the boot script.
/// </summary>
/// <remarks>
/// The script used to carry its own copy of every slug, accent key and look, held to the C# by tests;
/// now there is no copy to drift. The block is never executed, so the page's script policy has
/// nothing to say about it. The default encoder escapes <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>,
/// so nothing in it can close the element it sits in.
/// </remarks>
public static class DeskBoot
{
    /// <summary>The <c>id</c> of the data block, which theme-boot.js reads by.</summary>
    public const string ElementId = "desk-boot";

    /// <summary>The tables, serialised once: they are the catalogue's, and the catalogue does not move.</summary>
    public static string Json { get; } = JsonSerializer.Serialize(Tables());

    /// <summary>
    /// <list type="bullet">
    /// <item><c>families</c> — the stored family name, in lower case, to its slug.</item>
    /// <item><c>standard</c>, <c>contrast</c> — the slugs a desk wears unchosen, and under Increase Contrast.</item>
    /// <item><c>accents</c> — by slug, the keys with a block of their own: every accent but the family's own.
    /// A family that offers only its own has no entry.</item>
    /// <item><c>looks</c> — each look's storage key, attribute, stored name (lower case) to the value it
    /// stamps, and the media query that takes it off.</item>
    /// </list>
    /// </summary>
    private static object Tables() => new Dictionary<string, object>
    {
        ["families"] = DeskThemeFamilies.Catalog.ToDictionary(f => f.Family.ToString().ToLowerInvariant(), f => f.Slug),
        ["standard"] = DeskThemeFamilies.Slug(DeskThemeFamily.Standard),
        ["contrast"] = DeskThemeFamilies.Slug(DeskThemeFamily.HighContrast),
        ["accents"] = DeskThemeFamilies.Catalog
            .Where(f => f.Accents.Count > 1)
            .ToDictionary(f => f.Slug, f => f.Accents.Skip(1).Select(a => a.Key).ToArray()),
        ["looks"] = DeskLooks.All.Select(look => new Dictionary<string, object?>
        {
            ["key"] = look.StorageKey,
            ["attribute"] = look.Attribute,
            ["values"] = look.Options
                .Where(o => o.Attribute is not null)
                .ToDictionary(o => o.Stored.ToLowerInvariant(), o => o.Attribute),
            ["yieldsTo"] = look.YieldsTo
        }).ToArray()
    };
}
