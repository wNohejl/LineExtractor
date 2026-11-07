using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Tests.Support;
using LineOps.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = LineOps.Desk.Windowing.Desk;

namespace LineOps.Desk.Tests;

/// <summary>
/// An empty desk offers its primary window and, beside it, the catalogue's first workspace by the
/// name the catalogue gives it. The Desk names no workspace of its own: a host whose catalogue has
/// none is offered none, rather than a key that fails when pressed.
/// </summary>
public class EmptyDeskTests : DeskTestContext
{
    private IRenderedComponent<DeskShell> Empty(IWindowCatalog catalog)
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
        Services.AddSingleton(catalog);
        return RenderComponent<DeskShell>();
    }

    [Fact]
    public void The_workspace_key_is_named_by_the_catalogue()
    {
        var cut = Empty(new FakeWindowCatalog());

        var keys = cut.Find(".desk-empty").QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

        Assert.Equal(["Open One", "Start"], keys);
    }

    [Fact]
    public void A_catalogue_with_no_workspace_offers_none()
    {
        var plain = new FakeWindowCatalog();
        var cut = Empty(new FakeWindowCatalog(plain.All, workspaces: [], defaultPrimary: FakeWindowCatalog.One));

        var keys = cut.Find(".desk-empty").QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

        Assert.Equal(["Open One"], keys);
    }

    [Fact]
    public void Pressing_the_workspace_key_opens_its_windows()
    {
        var cut = Empty(new FakeWindowCatalog());

        cut.Find(".desk-empty").QuerySelectorAll("button")[1].Click();

        Assert.Contains(Services.GetRequiredService<WindowManager>().Windows, w => w.Definition.Key == FakeWindowCatalog.One);
    }
}
