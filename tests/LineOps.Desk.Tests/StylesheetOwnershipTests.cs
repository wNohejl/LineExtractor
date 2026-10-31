using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// Two stylesheets, one line between them. The Desk's sheets hold the system: desk.css its
/// tokens, windows, panel anatomy and grouped form, controls.css the components' own rules (the
/// steps and the tab body among them), themes.css and mud-bridge.css loaded after them. This
/// app's lineops.css holds its windows: anything named after a window or a domain. Read from
/// the source, comments left out, because a comment may name a selector the other side owns.
/// </summary>
public class StylesheetOwnershipTests
{
    private static string Desk(string name) =>
        WithoutComments(File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", name)));

    private static string App() =>
        WithoutComments(File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "wwwroot", "css", "lineops.css")));

    private static string WithoutComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    [Theory]
    [InlineData(".price")]
    [InlineData(".board__")]
    [InlineData(".scoreboard")]
    [InlineData(".teammark")]
    [InlineData(".wager-")]
    [InlineData(".slip")]
    [InlineData(".journal-")]
    [InlineData(".ops")]
    [InlineData(".gamelines")]
    [InlineData(".perf-")]
    [InlineData(".movement")]
    [InlineData(".runbook")]
    [InlineData(".formrun")]
    [InlineData(".game-search")]
    [InlineData(".logrow__opp")]
    [InlineData(".splits")]
    [InlineData(".snippet__market")]
    [InlineData(".wcard")]
    [InlineData(".stake-row")]
    [InlineData(".book-picker")]
    [InlineData(".selection-")]
    public void A_window_s_selector_lives_in_the_app_sheet(string selector)
    {
        Assert.DoesNotContain(selector, Desk("desk.css"));
        Assert.DoesNotContain(selector, Desk("controls.css"));
        Assert.DoesNotContain(selector, Desk("themes.css"));
        Assert.DoesNotContain(selector, Desk("mud-bridge.css"));
        Assert.Contains(selector, App());
    }

    // A component the Desk ships is styled by the Desk: DeskSteps, DeskTabs and DeskGrid emit
    // these names, so the rules that draw them travel with the package, not with one app.
    [Theory]
    [InlineData(".wsteps {")]
    [InlineData(".wstep__dot")]
    [InlineData(".wstep__title")]
    [InlineData(".wstep__change")]
    [InlineData(".wstep__hint")]
    [InlineData(".tabs__panel")]
    [InlineData(".deskgrid__child")]
    public void A_desk_component_s_selector_lives_in_a_desk_sheet(string selector)
    {
        Assert.Contains(selector, Desk("controls.css") + Desk("desk.css"));
        Assert.DoesNotContain(selector, App());
    }

    [Theory]
    [InlineData(".wsections")]
    [InlineData(".wsection")]
    [InlineData(".wgroup")]
    [InlineData(".wgroup__row--form")]
    [InlineData(".wgroup__head")]
    [InlineData(".wgroup__foot")]
    [InlineData(".wfoot")]
    [InlineData(".check__mark")]
    [InlineData(".pform")]
    [InlineData(".desk-btn")]
    [InlineData(".deskgrid")]
    [InlineData(".sparkline")]
    [InlineData(".wm-")]
    [InlineData(".parts")]
    [InlineData(".tag__live")]
    public void A_system_selector_lives_in_desk_css(string selector) => Assert.Contains(selector, Desk("desk.css"));

    [Fact]
    public void The_desk_sheet_is_desk_css_and_the_app_sheet_loads_last()
    {
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "lineops.css")));

        // The Desk's sheets are DeskHead's to link, desk.css first and mud-bridge.css last of them;
        // the app links its own after DeskHead. The links, not the comments, which name the sheets
        // in prose.
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "DeskHead.razor"));
        var desk = head.IndexOf("Css(\"desk.css\")", StringComparison.Ordinal);
        var bridge = head.IndexOf("Css(\"mud-bridge.css\")", StringComparison.Ordinal);
        Assert.True(desk >= 0 && bridge > desk, $"desk {desk}, mud-bridge {bridge}");

        var app = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "App.razor"));
        Assert.DoesNotContain("_content/LineOps.Desk/css/", app);
        var deskHead = app.IndexOf("<DeskHead />", StringComparison.Ordinal);
        var own = app.IndexOf("\"css/lineops.css\"]", StringComparison.Ordinal);
        Assert.True(deskHead >= 0 && own > deskHead, $"DeskHead {deskHead}, app {own}");
    }
}
