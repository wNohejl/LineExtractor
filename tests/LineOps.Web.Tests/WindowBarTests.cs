using AngleSharp.Dom;
using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The toolbar's one rule: it does not move.
///
/// <para>
/// Every catalogue entry is drawn once, always, in catalogue order, whether or not its window
/// is open. State is painted onto that fixed furniture rather than changing what the furniture
/// is. The arrangement before this appended open windows as tabs on the left, so the strip
/// rearranged itself on every open and close — the key you were reaching for slid sideways,
/// and a subject window with a long name pushed the whole catalogue along with it.
/// </para>
///
/// <para>
/// Subject windows — a game, a team, a player, a head-to-head — appear nowhere in the strip.
/// They are about a subject rather than a destination, several can exist at once, and their
/// names are long. They are reachable as columns on the desk, which is where they already are.
/// </para>
/// </summary>
public class WindowBarTests : DeskTestContext
{
    private WindowManager NewDesk()
    {
        var manager = new WindowManager(new AppWindowCatalog());
        Services.AddSingleton(manager);
        return manager;
    }

    private static string[] Keys(IRenderedFragment bar) => bar
        .FindAll(".bar__key")
        .Select(k => k.GetAttribute("aria-label") ?? string.Empty)
        .ToArray();

    private static string[] Names(IRenderedFragment bar) => bar
        .FindAll(".bar__key .bar__key-name")
        .Select(t => t.TextContent.Trim())
        .ToArray();

    private static IElement KeyFor(IRenderedFragment bar, string title) => bar
        .FindAll(".bar__key")
        .Single(k => k.QuerySelector(".bar__key-name")?.TextContent.Trim() == title);

    [Fact]
    public void Every_openable_window_gets_exactly_one_key()
    {
        NewDesk();

        var bar = RenderComponent<WindowBar>();
        var openable = WindowCatalog.All.Count(d => !d.RequiresSubject);

        Assert.Equal(openable, Keys(bar).Length);
    }

    /// <summary>
    /// The property the whole change exists for: opening something must not reorder anything.
    /// </summary>
    [Fact]
    public void Opening_a_window_leaves_every_key_where_it_was()
    {
        var manager = NewDesk();

        var bar = RenderComponent<WindowBar>();
        var before = Names(bar);

        manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);
        bar.Render();

        Assert.Equal(before, Names(bar));
    }

    [Fact]
    public void An_open_window_is_marked_on_its_own_key_rather_than_given_a_second_one()
    {
        var manager = NewDesk();
        manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);

        var bar = RenderComponent<WindowBar>();

        // One key, wearing the open marking — not a key plus a tab.
        Assert.Single(bar.FindAll(".bar__key--open"));
        Assert.Contains("bar__key--open", KeyFor(bar, "Board").ClassName);
    }

    [Fact]
    public void The_focused_window_is_the_current_key_and_the_marbles_resting_place()
    {
        var manager = NewDesk();

        manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);
        var ops = manager.Open(WindowCatalog.Find(WindowCatalog.Ops)!);
        manager.Focus(ops.Id);

        var bar = RenderComponent<WindowBar>();
        var current = bar.FindAll(".bar__key--current");

        Assert.Single(current);
        Assert.Equal("Ops", current[0].QuerySelector(".bar__key-name")!.TextContent.Trim());

        Assert.Equal("true", current[0].GetAttribute("aria-current"));

        // The marble travels across the group keys, so it rests on the group that holds the
        // window you are actually in — Operations for Ops — and on no other.
        var resting = Assert.Single(bar.FindAll("[data-glide-rest]"));

        Assert.Contains("bar__group--current", resting.ClassName);
        Assert.Equal("Operations", resting.QuerySelector(".bar__group-name")!.TextContent.Trim());
    }

    /// <summary>
    /// The strip is folded: one key per catalogue group, and the window keys hang under it
    /// in a drawer. A drawer's key sums the state of what it holds, so a critical Ops shows
    /// on the Operations key even while the drawer is shut.
    /// </summary>
    [Fact]
    public void A_group_key_wears_the_state_of_the_windows_in_its_drawer()
    {
        var manager = NewDesk();
        var ops = manager.Open(WindowCatalog.Find(WindowCatalog.Ops)!);

        manager.SetPulse(ops.Id, PulseState.Critical, "breached");

        var bar = RenderComponent<WindowBar>();

        var groups = bar.FindAll(".bar__group");
        var operations = groups.Single(g => g.QuerySelector(".bar__group-name")!.TextContent.Trim() == "Operations");

        Assert.Contains("bar__group--open", operations.ClassName);
        Assert.Contains("pulse--critical", operations.QuerySelector(".bar__group-pulse")!.ClassName);

        // The other drawers are quiet, and every drawer holds its own group's keys.
        Assert.Single(groups, g => g.ClassList.Contains("bar__group--open"));

        foreach (var panel in bar.FindAll(".bar__menu-panel"))
        {
            var group = panel.GetAttribute("aria-label");
            var expected = WindowCatalog.All.Where(d => !d.RequiresSubject && d.Group == group).Select(d => d.Title).ToArray();
            var actual = panel.QuerySelectorAll(".bar__key-name").Select(n => n.TextContent.Trim()).ToArray();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Closing_a_window_clears_its_marking_without_moving_the_key()
    {
        var manager = NewDesk();
        var board = manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);

        var bar = RenderComponent<WindowBar>();
        var before = Names(bar);

        manager.Close(board.Id);
        bar.Render();

        Assert.Equal(before, Names(bar));
        Assert.Empty(bar.FindAll(".bar__key--open"));
        Assert.Contains("Open Board", Keys(bar));
    }

    /// <summary>
    /// A window's state has to reach the strip: an unfocused column that has started work, or
    /// gone critical, is exactly the one you are not looking at.
    /// </summary>
    [Fact]
    public void An_open_key_carries_its_windows_pulse()
    {
        var manager = NewDesk();
        var board = manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);

        manager.SetPulse(board.Id, PulseState.Critical, "breached");

        var bar = RenderComponent<WindowBar>();
        var pulse = KeyFor(bar, "Board").QuerySelector(".bar__pulse");

        Assert.NotNull(pulse);
        Assert.Contains("pulse--critical", pulse!.ClassName);
    }

    [Fact]
    public void A_closed_key_carries_no_pulse_because_it_has_no_window_to_report_on()
    {
        NewDesk();

        var bar = RenderComponent<WindowBar>();

        Assert.Empty(bar.FindAll(".bar__pulse"));
    }

    [Fact]
    public void A_collapsed_window_says_so_rather_than_reading_as_another_column()
    {
        var manager = NewDesk();
        var board = manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);

        manager.ToggleMinimise(board.Id);

        var bar = RenderComponent<WindowBar>();

        Assert.Contains("bar__key--collapsed", KeyFor(bar, "Board").ClassName);
        Assert.Contains("Restore Board", Keys(bar));
    }

    /// <summary>
    /// Subject windows get no key at all — not a closed one, and not a tab. Appending them is
    /// what made the strip unreadable, and they cannot be opened cold in any case.
    /// </summary>
    [Fact]
    public void A_destination_never_appears_in_the_strip()
    {
        var manager = NewDesk();
        var subjects = WindowCatalog.All.Where(d => d.RequiresSubject).ToList();

        Assert.NotEmpty(subjects);

        foreach (var subject in subjects)
            manager.Open(subject, titleOverride: "Boston Red Sox at New York Yankees");

        var bar = RenderComponent<WindowBar>();
        var names = Names(bar);

        Assert.DoesNotContain("Boston Red Sox at New York Yankees", names);
        Assert.Equal(WindowCatalog.All.Count(d => !d.RequiresSubject), names.Length);
    }

    [Fact]
    public void The_keys_are_grouped_the_way_the_catalogue_groups_them()
    {
        NewDesk();

        var bar = RenderComponent<WindowBar>();

        var rendered = bar.FindAll(".bar__group-name")
            .Select(g => g.TextContent.Trim())
            .ToArray();

        var expected = WindowCatalog.All
            .Where(d => !d.RequiresSubject)
            .Select(d => d.Group)
            .Distinct()
            .ToArray();

        Assert.Equal(expected, rendered);
    }

    // ---- The eviction notice, beside the count it is about ------------------------

    /// <summary>
    /// A window closed to make room is reported beside the count that made it happen, where the
    /// operator is looking when they open something — not in the footer's bottom edge, which the
    /// HIG keeps for status that is never critical. One line, its full sentence as the title, a
    /// key to bring the window back and one to dismiss.
    /// </summary>
    [Fact]
    public void Eviction_notice_renders_in_the_header()
    {
        var desk = NewDesk();
        Services.AddSingleton<LineOps.Desk.Primitives.IDeskAlerts>(new Support.FakeAlerts());
        desk.UpdateSettings(s => s.MaxConcurrentWindows = 2);
        desk.Open(WindowCatalog.Find(WindowCatalog.Journal)!);
        desk.Open(WindowCatalog.Find(WindowCatalog.Ops)!);
        desk.Open(WindowCatalog.Find(WindowCatalog.Runs)!);
        Assert.NotNull(desk.LastEviction);

        var header = RenderComponent<DeskHeader>();
        var footer = RenderComponent<DeskFooter>();

        var notice = header.Find(".hdr__notice");
        Assert.Equal("status", notice.GetAttribute("role"));
        Assert.Equal(desk.LastEviction, notice.QuerySelector(".hdr__notice-text")!.GetAttribute("title"));
        Assert.Equal("Reopen Journal", notice.QuerySelector(".hdr__notice-undo")!.TextContent.Trim());
        Assert.Equal("Dismiss", notice.QuerySelector(".hdr__notice-dismiss")!.GetAttribute("aria-label"));

        // Beside the count, inside the desk's own controls.
        Assert.Equal("hdr__count", notice.NextElementSibling?.ClassList.FirstOrDefault()
                                   ?? notice.PreviousElementSibling?.ClassList.FirstOrDefault());

        Assert.Empty(footer.FindAll("[role='status']"));
        Assert.DoesNotContain(desk.LastEviction!, footer.Markup);

        header.Find(".hdr__notice-dismiss").Click();
        Assert.Null(desk.LastEviction);
        Assert.Empty(header.FindAll(".hdr__notice"));
    }

    // ---- Window controls --------------------------------------------------------

    /// <summary>
    /// Minimise, maximise and close are the desk's own plain icon keys, glyphs drawn — not grey
    /// dots that half-imitate a Mac's traffic lights in a Windows order. Each still names its
    /// window to a screen reader and its action to a pointer, and Close does not ask the glide
    /// plate for the stop tone: dismissing a window is not destroying anything.
    /// </summary>
    [Fact]
    public void Window_controls_are_labelled_keys()
    {
        var desk = NewDesk();
        Services.AddSingleton<LineOps.Desk.Primitives.IDeskAlerts>(new Support.FakeAlerts());

        // The chrome is what is under test, so the window hosts a panel with no services to find.
        var notes = desk.Open(new WindowDefinition
        {
            Key = "notes",
            Title = "Notes",
            Icon = "",
            ComponentType = typeof(UnsavedInputTests.DraftPanel)
        });

        var window = RenderComponent<AppWindow>(p => p.Add(x => x.Window, notes));

        var keys = window.FindAll(".win__controls > button");
        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.Contains("win__ctl", k.ClassList));
        Assert.All(keys, k => Assert.NotNull(k.QuerySelector("svg")));
        Assert.Equal(["Minimise", "Take the whole row", "Close"], keys.Select(k => k.GetAttribute("title")));
        Assert.Equal(["Minimise Notes", "Maximise Notes", "Close Notes"], keys.Select(k => k.GetAttribute("aria-label")));
        Assert.All(keys, k => Assert.Null(k.GetAttribute("data-glide-tone")));

        var css = File.ReadAllText(Path.Combine(Support.Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "lineops.css"));
        Assert.DoesNotContain(".win__ctl::before", css);
        Assert.DoesNotMatch(@"\.win__ctl[\w-]*:hover[^{]*\{[^}]*state-negative", css);
        Assert.Matches(@"\.win__ctl\s*\{[^}]*width:\s*24px[^}]*height:\s*24px[^}]*color:\s*var\(--text-secondary\)", css);
        Assert.Matches(@"\.win__ctl:hover\s*\{[^}]*background:\s*var\(--surface-3\)", css);
    }
}
