using System.Text.RegularExpressions;
using Bunit;
using LineOps.Desk.Layout;
using LineOps.Desk.Primitives;
using LineOps.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = LineOps.Desk.Windowing.Desk;

namespace LineOps.Desk.Tests;

/// <summary>
/// The chrome around the windows, read as a reader and a keyboard meet it: the desk is the
/// page's main landmark, the connection-lost banner is a toast with a named dismiss key that
/// stays out of the page until it is shown, and the reconnect notice speaks in the Desk's voice.
/// </summary>
public class DeskChromeAccessibilityTests : DeskTestContext
{
    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Repo.Root(), "src", .. path]));

    [Fact]
    public void The_desk_is_the_main_landmark()
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());

        var desk = RenderComponent<DeskShell>().Find("[data-desk]");

        Assert.Equal("main", desk.TagName, ignoreCase: true);
        Assert.Contains("desk", desk.ClassList);
        Assert.Equal("Desk", desk.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_new_application_focuses_the_main_landmark_on_navigation()
    {
        var routes = Read("LineOps.Desk.Templates", "content", "deskapp", "Components", "Routes.razor");

        Assert.Contains("Selector=\"main\"", routes);
    }

    [Fact]
    public void The_error_banner_dismisses_with_a_named_key()
    {
        var layout = RenderComponent<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "body"))));
        var banner = layout.Find("#blazor-error-ui");

        var dismiss = banner.QuerySelector("button.dismiss")!;
        Assert.NotNull(dismiss);
        Assert.Equal("button", dismiss.GetAttribute("type"));
        Assert.Equal("Dismiss", dismiss.GetAttribute("aria-label"));
        Assert.NotNull(dismiss.QuerySelector("svg"));

        Assert.NotNull(banner.QuerySelector("a.reload"));
        Assert.Contains("The connection to the desk failed. Reload to continue.", banner.TextContent);
    }

    /// <summary>
    /// Blazor shows the banner by writing <c>display: block</c> inline, so the sheet hides it and
    /// floats it; nothing else may set its display, or it would sit in the page (and in the
    /// accessibility tree) while nothing is wrong.
    /// </summary>
    [Fact]
    public void The_error_banner_is_hidden_until_shown_and_floats_like_a_toast()
    {
        var css = DeskConventions.WithoutComments(Read("LineOps.Desk", "wwwroot", "css", "desk.css"));
        var rule = Regex.Match(css, @"#blazor-error-ui\s*\{[^}]*\}").Value;

        Assert.NotEmpty(rule);
        Assert.Contains("display: none", rule);
        Assert.Contains("position: fixed", rule);
        Assert.Contains("z-index: var(--z-toast)", rule);
        Assert.DoesNotMatch(@"#blazor-error-ui\s*\{[^}]*display:(?>\s*)(?!none)", css);
    }

    [Fact]
    public void The_reconnect_notice_uses_the_desk_s_ellipsis_and_type()
    {
        var markup = Read("LineOps.Desk", "Layout", "ReconnectModal.razor");
        Assert.DoesNotContain("...", markup);

        var css = DeskConventions.WithoutComments(Read("LineOps.Desk", "Layout", "ReconnectModal.razor.css"));
        var button = Regex.Match(css, @"#components-reconnect-modal button\s*\{[^}]*\}").Value;

        Assert.Contains("font: inherit", button);
        Assert.Contains("height: 28px", button);
        Assert.Contains("border-radius: var(--radius)", button);
        Assert.DoesNotMatch(@"border-radius:\s*[\d.]+(px|rem)", css);
    }
}
