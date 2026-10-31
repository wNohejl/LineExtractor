using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// Two stylesheets, one line between them. The Desk's sheets hold the system: desk.css its
/// tokens, windows, panel anatomy and grouped form, controls.css the components' own rules (the
/// steps and the tab body among them), themes.css and mud-bridge.css loaded after them. An
/// application's sheet holds its windows. This is the Desk's side of the line; the application's
/// side is <see cref="DeskConventions"/>, which the application runs against its own sheet. Read
/// from the source, comments left out, because a comment may name a selector the other side owns.
/// </summary>
public class StylesheetOwnershipTests
{
    private static string Desk(string name) =>
        DeskConventions.WithoutComments(File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", name)));

    public static TheoryData<string> DeskComponentSelectors => [.. DeskConventions.DeskComponentSelectors];

    // A component the Desk ships is styled by the Desk: DeskSteps, DeskTabs and DeskGrid emit
    // these names, so the rules that draw them travel with the package, not with one app. That an
    // app's sheet leaves them alone is DeskConventions.AppSheetLeavesDeskComponentsAlone.
    [Theory]
    [MemberData(nameof(DeskComponentSelectors))]
    public void A_desk_component_s_selector_lives_in_a_desk_sheet(string selector)
        => Assert.Contains(selector, Desk("controls.css") + Desk("desk.css"));

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
    public void The_desk_sheet_is_desk_css_and_deskhead_links_the_desk_s_sheets_in_order()
    {
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "lineops.css")));

        // The Desk's sheets are DeskHead's to link, desk.css first and mud-bridge.css last of them;
        // the app links its own after DeskHead (DeskConventions.AppHeadLoadsDeskFirst). The links,
        // not the comments, which name the sheets in prose.
        var head = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "DeskHead.razor"));
        var desk = head.IndexOf("Css(\"desk.css\")", StringComparison.Ordinal);
        var bridge = head.IndexOf("Css(\"mud-bridge.css\")", StringComparison.Ordinal);
        Assert.True(desk >= 0 && bridge > desk, $"desk {desk}, mud-bridge {bridge}");
    }
}
