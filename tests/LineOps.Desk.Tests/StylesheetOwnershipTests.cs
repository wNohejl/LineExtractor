using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// Two stylesheets, one line between them. The Desk's sheets (desk.css and the three loaded
/// after it) hold the system: tokens, windows, controls, panel anatomy, the grouped form. This
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
    [InlineData(".wstep")]
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
