using Bunit;
using LineOps.Desk;
using LineOps.Desk.Layout;
using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// What a consumer writes to get the Desk: <c>AddDesk</c> in <c>Program.cs</c>, and
/// <c>&lt;DeskHead /&gt;</c> and <c>&lt;DeskScripts /&gt;</c> in <c>App.razor</c>. Everything else —
/// MudBlazor's services, the stylesheet order, the scripts — is the Desk's to get right, so these
/// tests read the head it renders and the two files that consume it.
/// </summary>
public class DeskWiringTests : DeskTestContext
{
    private static readonly string App = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "App.razor"));
    private static readonly string Program = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Program.cs"));

    // The order is load-bearing: each sheet overrides the one before it on source order, and
    // the theme has to be on <html> before the first of them paints.
    [Fact]
    public void DeskHead_emits_the_sheets_in_load_order()
    {
        var head = RenderComponent<DeskHead>();

        var sources = head.FindAll("link[rel=stylesheet], script[src]")
            .Select(e => e.GetAttribute("href") ?? e.GetAttribute("src")!)
            .ToList();

        string[] expected =
        [
            "js/theme-boot.js",
            "MudBlazor.min.css",
            "css/desk.css",
            "css/controls.css",
            "css/themes.css",
            "css/mud-bridge.css",
        ];

        Assert.Equal(expected.Length, sources.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.EndsWith(expected[i], sources[i]);
    }

    // Every page sets its text in the regular face, and desk.css only names it once the sheet has
    // arrived and parsed, so without a hint the font is the last thing the head asks for and the
    // first paint is in the fallback. The italic is set in two places and is left to load when one
    // of them draws.
    [Fact]
    public void DeskHead_preloads_the_body_face_before_the_first_stylesheet()
    {
        var head = RenderComponent<DeskHead>();

        var preload = Assert.Single(head.FindAll("link[rel=preload]"));
        Assert.EndsWith("fonts/InterVariable.woff2", preload.GetAttribute("href"));
        Assert.Equal("font", preload.GetAttribute("as"));
        Assert.Equal("font/woff2", preload.GetAttribute("type"));

        // A font is always fetched in CORS mode; a preload without crossorigin is a different
        // request, and the browser fetches the face a second time.
        Assert.True(preload.HasAttribute("crossorigin"));

        var tags = head.FindAll("link, script").ToList();
        Assert.True(tags.IndexOf(preload) < tags.FindIndex(e => e.GetAttribute("rel") == "stylesheet"),
            "The font preload comes after a stylesheet, which it was meant to overtake.");
    }

    // The preload has to be the request the @font-face makes, and a stylesheet's url() is not
    // fingerprinted: through @Assets the preload would name InterVariable.<hash>.woff2 and the
    // face would be downloaded twice. Read from the source, because bUnit's Assets fingerprints
    // nothing and the rendered href looks right either way.
    [Fact]
    public void The_font_preload_names_the_path_the_stylesheet_asks_for()
    {
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "DeskHead.razor"));
        var desk = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "desk.css"));

        var preload = Assert.Single(head.Split('\n'), l => l.Contains("<link rel=\"preload\""));
        Assert.DoesNotContain("@Assets[", preload);
        Assert.Contains("/fonts/InterVariable.woff2", preload);
        Assert.Contains("url('../fonts/InterVariable.woff2')", desk);
    }

    [Fact]
    public void DeskScripts_loads_MudBlazor_before_the_desk_s_own()
    {
        var scripts = RenderComponent<DeskScripts>().FindAll("script[src]")
            .Select(e => e.GetAttribute("src")!)
            .ToList();

        Assert.Collection(scripts,
            s => Assert.EndsWith("MudBlazor.min.js", s),
            s => Assert.EndsWith("js/desk-glide.js", s));
    }

    [Fact]
    public void App_wires_the_desk_with_two_components()
    {
        Assert.Contains("<DeskHead />", App);
        Assert.Contains("<DeskScripts />", App);
        Assert.DoesNotContain("_content/LineOps.Desk/css/", App);
        Assert.DoesNotContain("_content/MudBlazor/", App);
    }

    // AddDesk registers MudBlazor's services with the Desk's snackbar defaults; a second call in
    // the app would register them twice and the second configuration would be the one that held.
    [Fact]
    public void Program_does_not_call_AddMudServices()
        => Assert.DoesNotContain("AddMudServices", Program);

    [Fact]
    public void The_palette_placeholder_is_the_brand_s()
    {
        Services.AddSingleton(new DeskBrand("LINE", "OPS", "test desk", "Find a thing"));
        var palette = RenderComponent<CommandPalette>();
        Services.GetRequiredService<WindowManager>().RequestPalette();
        palette.WaitForState(() => palette.FindAll("[role=dialog]").Count == 1);

        Assert.Equal("Find a thing", palette.Find("input").GetAttribute("placeholder"));
    }

    [Fact]
    public void A_brand_that_names_no_placeholder_says_search()
        => Assert.Equal("Search", new DeskBrand("A", "B", "c").SearchPlaceholder);

    [Fact]
    public void AddDesk_registers_MudBlazor_with_the_desk_s_snackbar_and_lets_the_app_change_it()
    {
        var services = new ServiceCollection();
        services.AddDesk(new DeskBrand("A", "B", "c"), mud: o => o.SnackbarConfiguration.MaxDisplayedSnackbars = 2);

        using var provider = services.BuildServiceProvider();
        var snackbar = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MudBlazor.SnackbarConfiguration>>().Value;

        Assert.Equal(MudBlazor.Defaults.Classes.Position.BottomRight, snackbar.PositionClass);
        Assert.Equal(120, snackbar.ShowTransitionDuration);
        Assert.Equal(2, snackbar.MaxDisplayedSnackbars);
    }
}
