using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using XLines.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = XLines.Desk.Windowing.Desk;

namespace XLines.Desk.Tests;

/// <summary>
/// An empty desk guides to a workflow: a headline and a sentence, then the catalogue's workspaces
/// in its order — the first featured with the page's one Filled key, each with its icon, its
/// sentence and the windows it opens — then the ones saved here, then a quieter way to open a
/// single window, and the desk's mechanics folded away. Every word comes from the catalogue the
/// person at the desk may see; the Desk names no workspace of its own.
/// </summary>
public class EmptyDeskTests : DeskTestContext
{
    private static WindowDefinition Def(string key, string title, bool operatorOnly = false) => new()
    {
        Key = key,
        Title = title,
        Icon = "icon-" + key,
        ComponentType = typeof(FakeWindowCatalog.Blank),
        OperatorOnly = operatorOnly,
    };

    private static readonly WindowDefinition[] Windows =
    [
        Def("a", "Alpha"),
        Def("b", "Bravo"),
        Def("c", "Charlie"),
        Def("x", "Secret", operatorOnly: true),
    ];

    private static FakeWindowCatalog Catalogue() => new(Windows,
    [
        new Workspace("Watch", "Keep an eye on things as they move.", ["a", "b"], Icon: "icon-watch"),
        new Workspace("Review", "", ["c", "a", "b"]),
        new Workspace("Back room", "Only for operators.", ["x"]),
        new Workspace("Mixed", "Some of it is private.", ["x", "c"]),
    ], defaultPrimary: "a");

    private IRenderedComponent<DeskShell> Empty(IWindowCatalog catalog, IDeskAccess? access = null)
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
        Services.AddSingleton(catalog);

        if (access is not null)
            Services.AddSingleton(access);

        return RenderComponent<DeskShell>();
    }

    private sealed class Visitor : IDeskAccess
    {
        public bool CanOperate => false;
    }

    [Fact]
    public void The_first_workspace_is_featured_with_the_page_s_one_filled_key()
    {
        var cut = Empty(Catalogue());
        var page = cut.Find(".desk-empty");

        Assert.Equal("Start with a workspace", page.QuerySelector("h1")!.TextContent);

        var featured = page.QuerySelector(".desk-empty__featured")!;
        Assert.Equal("Watch", featured.QuerySelector(".desk-empty__name")!.TextContent);
        Assert.Equal("Keep an eye on things as they move.", featured.QuerySelector(".desk-empty__desc")!.TextContent);
        Assert.Equal("Opens Alpha and Bravo", featured.QuerySelector(".desk-empty__windows")!.TextContent);
        Assert.Contains("icon-watch", featured.QuerySelector(".desk-empty__icon")!.InnerHtml);

        var filled = page.QuerySelectorAll(".desk-btn--filled");
        Assert.Single(filled);
        Assert.Equal("Open Watch", filled[0].TextContent.Trim());
        Assert.Same(featured, filled[0].Closest(".desk-empty__featured"));
    }

    [Fact]
    public void The_rest_follow_in_catalogue_order_and_say_what_they_open()
    {
        var cut = Empty(Catalogue());

        var rows = cut.FindAll(".desk-empty__list .desk-empty__row");
        Assert.Equal(["Review", "Back room", "Mixed"], rows.Select(r => r.QuerySelector(".desk-empty__name")!.TextContent));
        Assert.Equal("Opens Charlie, Alpha and Bravo", rows[0].QuerySelector(".desk-empty__windows")!.TextContent);
        Assert.Equal("Opens Secret and Charlie", rows[2].QuerySelector(".desk-empty__windows")!.TextContent);

        // Each row is one button: nothing inside it is a second target.
        Assert.All(rows, r => Assert.Equal("BUTTON", r.TagName));
        Assert.All(rows, r => Assert.Empty(r.QuerySelectorAll("button, a")));
        Assert.Equal("More workspaces", cut.Find(".desk-empty__heading").TextContent);

        // The outline: h1, then Workspaces (h2) over the featured one and the groups (h3).
        Assert.Equal(["H1", "H2", "H3", "H3"], cut.Find(".desk-empty").QuerySelectorAll("h1, h2, h3").Select(h => h.TagName));
        Assert.Equal("Workspaces", cut.Find(".desk-empty h2").TextContent);
    }

    [Fact]
    public void A_workspace_with_no_description_or_icon_still_reads()
    {
        var cut = Empty(Catalogue());

        var review = cut.FindAll(".desk-empty__row")[0];
        Assert.Null(review.QuerySelector(".desk-empty__desc"));
        Assert.NotNull(review.QuerySelector(".desk-empty__icon svg, .desk-empty__icon [class]"));
        Assert.NotNull(review.QuerySelector(".desk-empty__windows"));
    }

    [Fact]
    public void A_visitor_is_offered_only_what_they_may_open()
    {
        var cut = Empty(Catalogue(), new Visitor());

        var names = cut.FindAll(".desk-empty__row").Select(r => r.QuerySelector(".desk-empty__name")!.TextContent).ToList();
        Assert.Equal(["Review", "Mixed"], names);
        Assert.DoesNotContain("Secret", cut.Find(".desk-empty").TextContent);
        Assert.Equal("Opens Charlie", cut.FindAll(".desk-empty__row")[1].QuerySelector(".desk-empty__windows")!.TextContent);
    }

    [Fact]
    public void Choosing_a_workspace_opens_it_and_marks_it_active()
    {
        var cut = Empty(Catalogue());
        var manager = Services.GetRequiredService<WindowManager>();

        cut.FindAll(".desk-empty__row")[0].Click();

        Assert.Equal(["c", "a", "b"], manager.Windows.Select(w => w.Definition.Key));
        Assert.Equal("Review", manager.ActiveWorkspace?.Name);
    }

    [Fact]
    public void The_featured_key_opens_the_first_workspace()
    {
        var cut = Empty(new FakeWindowCatalog());

        cut.Find(".desk-empty__featured .desk-btn--filled").Click();

        Assert.Contains(Services.GetRequiredService<WindowManager>().Windows, w => w.Definition.Key == FakeWindowCatalog.One);
    }

    [Fact]
    public void A_single_window_and_the_palette_are_the_quieter_way_in()
    {
        var cut = Empty(Catalogue());

        var single = cut.Find(".desk-empty__single");
        var key = single.QuerySelector("button")!;
        Assert.Equal("Open Alpha", key.TextContent.Trim());
        Assert.DoesNotContain("desk-btn--filled", key.ClassName);
        Assert.Equal(["Ctrl", "K"], single.QuerySelectorAll("kbd").Select(k => k.TextContent));

        // The mechanics are folded away, not the lead paragraph.
        Assert.Equal("How the desk works", cut.Find(".desk-empty__how .disclosure__label").TextContent);
        Assert.Empty(cut.FindAll(".desk-empty__facts"));
    }

    [Fact]
    public void A_catalogue_with_no_workspace_makes_the_single_window_the_primary_action()
    {
        var plain = new FakeWindowCatalog();
        var cut = Empty(new FakeWindowCatalog(plain.All, workspaces: [], defaultPrimary: FakeWindowCatalog.One));

        Assert.Equal("Start with a window", cut.Find(".desk-empty h1").TextContent);
        Assert.Empty(cut.FindAll(".desk-empty__featured, .desk-empty__list"));
        var filled = Assert.Single(cut.FindAll(".desk-empty .desk-btn--filled"));
        Assert.Equal("Open One", filled.TextContent.Trim());
    }

    [Fact]
    public void Saved_workspaces_follow_the_application_s()
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
        Services.AddSingleton<IWindowCatalog>(Catalogue());
        var manager = Services.GetRequiredService<WindowManager>();
        manager.Restore(new DeskLayout
        {
            MaxConcurrentWindows = 3,
            Workspaces = [new DeskLayoutWorkspace("Mine", ["b"])],
        });

        var cut = RenderComponent<DeskShell>();

        var headings = cut.FindAll(".desk-empty__heading").Select(h => h.TextContent).ToList();
        Assert.Equal(["More workspaces", "Saved on this desk"], headings);
        Assert.Equal("Mine", cut.FindAll(".desk-empty__group")[1].QuerySelector(".desk-empty__name")!.TextContent);
    }

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    [InlineData(new[] { "A", "B", "C", "D", "E", "F" }, "A, B, C, D and 2 more")]
    public void The_windows_are_said_as_a_sentence(string[] titles, string sentence)
        => Assert.Equal(sentence, DeskShell.WindowsSentence(titles));
}
