namespace LineOps.Desk;

/// <summary>
/// Where the Desk's static files are served from: the <c>_content</c> path a Razor class library's
/// <c>wwwroot</c> is published under, named once so a script import and a stylesheet link cannot
/// drift apart.
/// </summary>
public static class DeskAssets
{
    /// <summary>The Desk's <c>wwwroot</c>, as the page sees it.</summary>
    public const string Root = "_content/LineOps.Desk";

    /// <summary>A stylesheet of the Desk's, for a <c>&lt;link&gt;</c> or <c>@Assets[…]</c>.</summary>
    public static string Css(string name) => $"{Root}/css/{name}";

    /// <summary>A script of the Desk's, for a <c>&lt;script src&gt;</c> or <c>@Assets[…]</c>.</summary>
    public static string Js(string name) => $"{Root}/js/{name}";

    /// <summary>
    /// A script of the Desk's as <c>IJSRuntime</c>'s <c>"import"</c> takes it. The leading
    /// <c>./</c> is what Blazor resolves against the document's base; without it the path is a
    /// bare module specifier, and the browser refuses to load it.
    /// </summary>
    public static string Module(string name) => $"./{Js(name)}";
}
