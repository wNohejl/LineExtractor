using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using XLines.Desk.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = XLines.Desk.Windowing.Desk;

namespace XLines.Desk.Tests;

/// <summary>
/// A tab group's title, drawn (ADR 0027, amended 2026-11-20): the window showing as a pop-up key
/// with the column's count, its menu of the column's windows most recently shown first, the
/// keyboard of a menu button, closing and moving from the menu, a window behind's state on the
/// closed key, and a panel's input surviving a switch.
/// </summary>
public class WindowTabStripTests : SampleDeskTestContext
{
    private static WindowDefinition Window(string key) => SampleCatalog.Find(key);

    private WindowManager Manager(int columns, IDeskAlerts? alerts = null)
    {
        Services.AddSingleton(alerts ?? new FakeAlerts());
        var manager = Services.GetRequiredService<WindowManager>();
        manager.UpdateSettings(s =>
        {
            s.MaxConcurrentWindows = columns;
            s.PrimaryWindowKey = null;
        });
        return manager;
    }

    private static AngleSharp.Dom.IElement Win(IRenderedComponent<DeskShell> desk, WindowInstance window)
        => desk.Find($"#win-{window.Id}");

    private static AngleSharp.Dom.IElement Key(IRenderedComponent<DeskShell> desk, WindowInstance showing)
        => Win(desk, showing).QuerySelector(".wtabs__key")!;

    private static List<string> MenuLabels(IRenderedComponent<DeskShell> desk, WindowInstance showing)
        => Win(desk, showing).QuerySelectorAll(".menu__item").Select(i => i.QuerySelector(".menu__label")!.TextContent).ToList();

    /// <summary>Four windows in one column: Jobs showing, then Alerts, Health, List behind it.</summary>
    private (WindowManager Manager, WindowInstance Jobs) FourInOne()
    {
        var manager = Manager(columns: 1);
        foreach (var key in new[] { SampleCatalog.List, SampleCatalog.Health, SampleCatalog.Alerts, SampleCatalog.Jobs })
            manager.Open(Window(key));

        return (manager, manager.Columns.Single().Showing);
    }

    [Fact]
    public void A_group_titles_itself_with_a_pop_up_key_and_hides_the_tab_behind()
    {
        var manager = Manager(columns: 1);
        var list = manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));

        var desk = RenderComponent<DeskShell>();
        var showing = Win(desk, health);

        // No strip: no tablist and no tab for the window behind anywhere on the bar.
        Assert.Empty(desk.FindAll("[role=tablist], [role=tab]"));

        // The key is the title: the showing window's name, the count and the pop-up's chevron.
        var key = Key(desk, health);
        Assert.Equal("menu", key.GetAttribute("aria-haspopup"));
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
        // Down and Up are the menu button's own keys, not shortcuts to announce.
        Assert.Null(key.GetAttribute("aria-keyshortcuts"));
        Assert.Equal("Health, 2 tabs in this column — show tabs", key.GetAttribute("aria-label"));
        Assert.Equal("Health", key.QuerySelector(".win__title")!.TextContent);
        Assert.Equal("2 tabs", key.QuerySelector(".wtabs__count")!.TextContent);
        Assert.NotNull(key.QuerySelector(".wtabs__chevron"));
        Assert.DoesNotContain("List", key.TextContent);

        // One title on the bar, inside the key; and the bar keeps the window's own Close.
        Assert.Single(showing.QuerySelectorAll(".win__title"));
        Assert.Equal("Close Health", showing.QuerySelector(".win__ctl--close")!.GetAttribute("aria-label"));
        Assert.Null(showing.QuerySelector(".win__body")!.GetAttribute("role"));

        // The window behind is on the page, kept, but out of sight, reach and the tree.
        var behind = Win(desk, list);
        Assert.Contains("win--behind", behind.ClassList);
        Assert.True(behind.HasAttribute("inert"));
        Assert.Equal("true", behind.GetAttribute("aria-hidden"));
        Assert.Null(behind.GetAttribute("role"));
        Assert.Null(behind.QuerySelector(".win__bar"));
        Assert.NotNull(behind.QuerySelector($"#winbody-{list.Id}"));
    }

    [Fact]
    public void A_group_of_one_is_a_plain_window()
    {
        var manager = Manager(columns: 2);
        var list = manager.Open(Window(SampleCatalog.List));

        var desk = RenderComponent<DeskShell>();

        Assert.Empty(desk.FindAll(".wtabs"));
        Assert.NotNull(Win(desk, list).QuerySelector(".win__title"));
        Assert.NotNull(Win(desk, list).QuerySelector(".win__ctl--close"));
    }

    [Fact]
    public void A_group_closed_down_to_one_window_becomes_a_plain_window_again()
    {
        var manager = Manager(columns: 1);
        var list = manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();

        Key(desk, health).Click();
        Win(desk, health).QuerySelectorAll(".menu__remove")[1].Click();

        Assert.DoesNotContain(list, manager.Windows);
        Assert.Empty(desk.FindAll(".wtabs"));
        Assert.NotNull(Win(desk, health).QuerySelector(".win__title"));
    }

    [Fact]
    public void The_menu_lists_the_column_most_recent_first_then_its_commands()
    {
        var (manager, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).Click();

        Assert.Equal("true", Key(desk, jobs).GetAttribute("aria-expanded"));
        Assert.Equal(["Jobs", "Alerts", "Health", "List", "Move Jobs to its own column", "Close Jobs"], MenuLabels(desk, jobs));

        var items = Win(desk, jobs).QuerySelectorAll(".menu__item");
        var menu = Win(desk, jobs).QuerySelector("[role=menu]")!;
        Assert.Equal("Tabs in Jobs's column", menu.GetAttribute("aria-label"));

        // The windows are the pop-up's options, the showing one checked; the rest are commands.
        Assert.Equal(["menuitemradio", "menuitemradio", "menuitemradio", "menuitemradio", "menuitem", "menuitem"],
            items.Select(i => i.GetAttribute("role")));
        Assert.Equal(["true", "false", "false", "false"], items.Take(4).Select(i => i.GetAttribute("aria-checked")));

        // Each window row carries a close for the pointer and Delete for the keyboard.
        Assert.All(items.Take(4), i => Assert.Equal("Delete", i.GetAttribute("aria-keyshortcuts")));
        Assert.All(items.Take(4), i => Assert.Equal("true", i.QuerySelector(".menu__remove")!.GetAttribute("aria-hidden")));
        Assert.Equal("Close Alerts", items[1].QuerySelector(".menu__remove")!.GetAttribute("title"));
        Assert.Null(items[4].QuerySelector(".menu__remove"));

        // One column and every column in use: Move says why it cannot.
        Assert.Equal("true", items[4].GetAttribute("aria-disabled"));
    }

    [Fact]
    public void Choosing_a_window_brings_it_forward_and_the_one_showing_steps_to_second()
    {
        var (manager, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).Click();
        Win(desk, jobs).QuerySelectorAll(".menu__item")[3].Click();

        var column = manager.Columns.Single();
        Assert.Equal(["List", "Jobs", "Alerts", "Health"], column.Tabs.Select(t => t.Title));
        Assert.Equal(column.Showing.Id, manager.FocusedId);
        Assert.Contains("win--behind", Win(desk, jobs).ClassList);

        // The key on the window now showing names it, and its menu is closed.
        var list = column.Showing;
        Assert.Equal("List", Key(desk, list).QuerySelector(".win__title")!.TextContent);
        Assert.Equal("false", Key(desk, list).GetAttribute("aria-expanded"));

        Key(desk, list).Click();
        Win(desk, list).QuerySelectorAll(".menu__item")[3].Click();
        Assert.Equal(["Health", "List", "Jobs", "Alerts"], manager.Columns.Single().Tabs.Select(t => t.Title));
    }

    [Fact]
    public void The_key_opens_with_Down_and_Escape_folds_the_menu()
    {
        var (_, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("true", Key(desk, jobs).GetAttribute("aria-expanded"));
        Assert.NotEmpty(Win(desk, jobs).QuerySelectorAll(".menu__item"));

        Win(desk, jobs).QuerySelector("[role=menu]")!.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("false", Key(desk, jobs).GetAttribute("aria-expanded"));
        Assert.Empty(Win(desk, jobs).QuerySelectorAll(".menu__item"));

        // The arrows walk the menu from the window showing; typeahead jumps by name.
        Key(desk, jobs).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        var menu = Win(desk, jobs).QuerySelector("[role=menu]")!;
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[0].GetAttribute("tabindex"));
        menu.KeyDown(new KeyboardEventArgs { Key = "h" });
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[2].GetAttribute("tabindex"));
        Win(desk, jobs).QuerySelector("[role=menu]")!.KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[5].GetAttribute("tabindex"));
    }

    [Fact]
    public void Delete_closes_the_focused_row_and_the_menu_stays_on_the_next()
    {
        var (manager, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).Click();
        Win(desk, jobs).QuerySelector("[role=menu]")!.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Win(desk, jobs).QuerySelector("[role=menu]")!.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        Assert.DoesNotContain(manager.Windows, w => w.Title == "Alerts");
        Assert.True(manager.IsShowing(jobs));
        Assert.Equal(["Jobs", "Health", "List", "Move Jobs to its own column", "Close Jobs"], MenuLabels(desk, jobs));

        // The Tab stop moved to the row that took the closed one's place.
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[1].GetAttribute("tabindex"));
    }

    [Fact]
    public void The_pointer_s_close_glyph_moves_the_keyboard_on_as_Delete_does()
    {
        var (manager, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).Click();
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[0].GetAttribute("tabindex"));

        Win(desk, jobs).QuerySelectorAll(".menu__remove")[1].Click(); // Alerts

        Assert.DoesNotContain(manager.Windows, w => w.Title == "Alerts");
        Assert.Equal(["Jobs", "Health", "List", "Move Jobs to its own column", "Close Jobs"], MenuLabels(desk, jobs));
        Assert.Equal("0", Win(desk, jobs).QuerySelectorAll(".menu__item")[1].GetAttribute("tabindex"));
    }

    [Fact]
    public void Close_from_the_menu_closes_the_window_showing_and_reveals_the_next()
    {
        var (manager, jobs) = FourInOne();
        var desk = RenderComponent<DeskShell>();

        Key(desk, jobs).Click();
        Win(desk, jobs).QuerySelectorAll(".menu__item")[5].Click();

        Assert.DoesNotContain(jobs, manager.Windows);
        Assert.Equal("Alerts", manager.Columns.Single().Showing.Title);
    }

    [Fact]
    public void Closing_a_tab_with_unsaved_input_asks_first()
    {
        var alerts = new FakeAlerts { Answer = false };
        var manager = Manager(columns: 1, alerts);
        var list = manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        manager.SetUnsaved(list.Id, true, "this note");
        var desk = RenderComponent<DeskShell>();

        Key(desk, health).Click();
        Win(desk, health).QuerySelectorAll(".menu__remove")[1].Click();

        Assert.Equal(["Discard this note?"], alerts.Asked);
        Assert.Contains(list, manager.Windows);
    }

    [Fact]
    public void Move_takes_the_window_showing_to_a_column_of_its_own_when_one_is_free()
    {
        var manager = Manager(columns: 1);
        manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        manager.UpdateSettings(s => s.MaxConcurrentWindows = 2);
        var desk = RenderComponent<DeskShell>();

        Key(desk, health).Click();
        var move = Win(desk, health).QuerySelectorAll(".menu__item")[2];
        Assert.Null(move.GetAttribute("aria-disabled"));
        move.Click();

        Assert.Equal(2, manager.ColumnCount);
        Assert.Empty(desk.FindAll(".wtabs"));
    }

    [Fact]
    public void A_window_behind_that_goes_critical_says_so_on_the_closed_key_and_its_row()
    {
        var manager = Manager(columns: 1);
        var list = manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();

        Assert.Null(Key(desk, health).QuerySelector(".wtabs__state"));
        Assert.Null(Key(desk, health).GetAttribute("title"));

        desk.InvokeAsync(() => manager.SetPulse(list.Id, PulseState.Critical, "3 failed"));

        var key = Key(desk, health);
        Assert.Contains("pulse--critical", key.QuerySelector(".wtabs__state")!.ClassList);
        Assert.Equal(key.GetAttribute("aria-label"), key.GetAttribute("title"));
        Assert.Equal("true", key.QuerySelector(".wtabs__state")!.GetAttribute("aria-hidden"));
        Assert.Equal("Health, 2 tabs in this column; List needs attention — show tabs", key.GetAttribute("aria-label"));

        key.Click();
        var row = Win(desk, health).QuerySelectorAll(".menu__item")[1];
        Assert.Equal("List — 3 failed, needs attention", row.GetAttribute("aria-label"));
        Assert.Equal("3 failed", row.QuerySelector(".menu__desc")!.TextContent);
        Assert.NotNull(row.QuerySelector(".menu__attention--negative"));

        // A warning is a warning, in its own hue and words.
        desk.InvokeAsync(() => manager.SetPulse(list.Id, PulseState.Warn, null));
        Assert.Contains("pulse--warn", Key(desk, health).QuerySelector(".wtabs__state")!.ClassList);
        Assert.EndsWith("List has a warning — show tabs", Key(desk, health).GetAttribute("aria-label"));

        // Told apart by shape, not hue alone: a ring for a warning, a dot for a breach, and both
        // kept in forced colours.
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.Matches(@"\.wtabs__state\.pulse--warn\s*\{[^}]*background:\s*transparent;[^}]*border:\s*1\.5px solid var\(--state-warning\)", css);
        Assert.Contains(":root :is(.wtabs__state.pulse--critical, .menu__attention--negative) { border: 4px solid CanvasText; }", css);
        var controls = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "controls.css"));
        Assert.Contains(".menu__attention--warning { border: 1.5px solid var(--state-warning); }", controls);
    }

    [Fact]
    public void The_header_shows_one_window_count_and_the_footer_agrees()
    {
        var manager = Manager(columns: 1);
        manager.Open(Window(SampleCatalog.List));
        manager.Open(Window(SampleCatalog.Health));

        var header = RenderComponent<DeskHeader>();
        var footer = RenderComponent<DeskFooter>();

        // One figure: every open window, tabbed ones included; no "1/1", no "+1 tabbed".
        var count = header.Find(".hdr__count");
        Assert.Equal("2 windows", count.QuerySelector("[aria-hidden=true]")!.TextContent);
        Assert.Equal("2 windows open, in 1 of 1 columns", count.QuerySelector(".sr-only")!.TextContent);
        Assert.Empty(header.FindAll(".hdr__count-tabs"));
        Assert.DoesNotContain("tabbed", count.TextContent);
        Assert.DoesNotContain("/", count.TextContent);

        // The footer says the same figure beside the columns, tabbed or not.
        Assert.Contains("1 column, 2 windows", footer.Find(".ftr__stat").TextContent);
        Assert.Equal("1 window", DeskHeader.WindowsLabel(1));
    }

    [Fact]
    public void The_footer_counts_windows_even_with_none_tabbed()
    {
        var manager = Manager(columns: 2);
        manager.Open(Window(SampleCatalog.List));

        var footer = RenderComponent<DeskFooter>();

        Assert.Contains("1 column, 1 window", footer.Find(".ftr__stat").TextContent);
    }

    [Fact]
    public void The_title_key_draws_an_edge_per_window_behind_two_at_most()
    {
        var manager = Manager(columns: 1);
        var list = manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();

        var tabs = Win(desk, health).QuerySelector(".wtabs")!;
        Assert.Contains("wtabs--depth-1", tabs.ClassList);
        Assert.Single(tabs.QuerySelectorAll(".wtabs__layer"));
        Assert.All(tabs.QuerySelectorAll(".wtabs__layer"), l => Assert.Equal("true", l.GetAttribute("aria-hidden")));

        // The layers come before the key, so it paints over them without a z-index.
        Assert.Contains("wtabs__layer", tabs.Children[0].ClassName);

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.Contains(".wtabs__layer--1 { translate: var(--tab-step) 0; scale: 1 .78;", css);
        Assert.Contains(".wtabs__layer--2 { translate: calc(2 * var(--tab-step)) 0; scale: 1 .56;", css);
        Assert.Contains(".win__bar--group .win__status { margin-left: var(--space-3); }", css);
        Assert.Contains("@container winbar (max-width: 300px) {\n    .wtabs__count-word { display: none; }", css.Replace("\r\n", "\n"));
        Assert.NotNull(list);
    }

    [Fact]
    public void Down_and_Up_on_the_key_do_not_also_scroll_the_box_it_sits_in()
    {
        var module = JSInterop.SetupModule(DeskAssets.Module("popover.js"));
        var manager = Manager(columns: 1);
        manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();

        Assert.NotNull(Key(desk, health));
        Assert.NotEmpty(module.Invocations["opensOnArrows"]);

        var js = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "js", "popover.js"));
        Assert.Matches(@"export function opensOnArrows\(key\)[\s\S]*?event\.preventDefault\(\)", js);
    }

    [Fact]
    public void A_held_tab_key_keeps_its_focus_ring()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.Matches(@"\.wtabs__menu \.wtabs__tab:focus-visible:active\s*\{\s*box-shadow:\s*inset 0 1px 2px var\(--key-press\), var\(--focus-ring\);", css);
        Assert.True(
            css.IndexOf(".wtabs__menu .wtabs__tab:focus-visible:active", StringComparison.Ordinal)
                > css.IndexOf(".wtabs__menu .wtabs__tab:active", StringComparison.Ordinal),
            "the ring's rule comes after the press rule it must outlast");
    }

    [Fact]
    public void Each_tab_in_the_menu_is_a_key_and_the_one_showing_is_selected()
    {
        var manager = Manager(columns: 1);
        manager.Open(Window(SampleCatalog.List));
        var health = manager.Open(Window(SampleCatalog.Health));
        var desk = RenderComponent<DeskShell>();

        Key(desk, health).Click();
        var rows = Win(desk, health).QuerySelectorAll(".menu__item");

        // The tabs are keys of their own; the commands below stay plain menu rows.
        var tabs = rows.Where(r => r.ClassList.Contains("wtabs__tab")).ToList();
        Assert.Equal(2, tabs.Count);
        Assert.Contains("wtabs__tab--showing", tabs[0].ClassList);
        Assert.Equal("true", tabs[0].GetAttribute("aria-checked"));
        Assert.DoesNotContain("wtabs__tab--showing", tabs[1].ClassList);
        Assert.Equal("false", tabs[1].GetAttribute("aria-checked"));
        Assert.All(rows.Where(r => r.TextContent.Contains("own column") || r.TextContent.Trim().StartsWith("Close")),
            r => Assert.DoesNotContain("wtabs__tab", r.ClassList));

        // A name cut to one line keeps its whole as the tooltip; the close is drawn at rest.
        Assert.Equal("Health", tabs[0].GetAttribute("title"));
        Assert.NotNull(tabs[1].QuerySelector(".menu__remove"));

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.Matches(@"\.wtabs__menu \.wtabs__tab\s*\{[^}]*background-image:\s*linear-gradient\(to bottom, var\(--key-gloss\), transparent 38%\)", css);
        Assert.Matches(@"\.wtabs__menu \.wtabs__tab--showing,\s*\.wtabs__menu \.wtabs__tab--showing:hover\s*\{\s*background-color:\s*var\(--accent-wash-strong\)", css);
        Assert.Matches(@"\.wtabs__tab \.menu__label,\s*\.wtabs__tab \.menu__desc\s*\{[^}]*white-space:\s*nowrap;[^}]*text-overflow:\s*ellipsis", css);
        // Selection, not a second Filled key.
        Assert.DoesNotContain("desk-btn--filled", Win(desk, health).QuerySelector(".wtabs__menu")!.OuterHtml);
    }

    [Fact]
    public void The_palette_brings_a_tab_behind_forward_rather_than_opening_another()
    {
        Services.AddSingleton<IDeskSearch>(new NoSearch());
        var manager = Manager(columns: 1);
        var health = manager.Open(Window(SampleCatalog.Health));
        manager.Open(Window(SampleCatalog.Jobs));
        var palette = RenderComponent<CommandPalette>();
        palette.InvokeAsync(manager.RequestPalette);
        palette.WaitForState(() => palette.FindAll("[role=option]").Count > 0);

        palette.FindAll("[role=option]").First(o => o.TextContent.Contains("Health")).Click();

        Assert.Equal(2, manager.OpenCount);
        Assert.True(manager.IsShowing(health));
    }

    [Fact]
    public void The_strip_and_its_width_fitting_are_gone()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        Assert.DoesNotContain(".wtab ", css);
        Assert.DoesNotContain(".wtab__", css);
        Assert.DoesNotContain(".wtabs__more", css);

        Assert.Null(typeof(WindowInstance).GetProperty("MeasuredWidth"));
        Assert.Null(typeof(WindowInstance).GetProperty("ShownWidth"));
        Assert.Null(typeof(WindowManager).GetMethod("SetMeasuredWidth"));
        Assert.Null(typeof(WindowTabs).GetMethod("VisibleTabs"));
    }

    private sealed class NoSearch : IDeskSearch
    {
        public Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DeskSearchResult>>([]);
    }

    // ------------------------------------------------------------ kept behind ----

    /// <summary>A panel that counts how often it was built and holds a draft, as a form does.</summary>
    public sealed class CountingDraft : DeskPanelBase
    {
        public static int Built;

        protected override void OnInitialized() => Built++;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "draft");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => MarkUnsaved(true, "this draft")));
            builder.CloseElement();
        }
    }

    [Fact]
    public void A_window_behind_a_tab_keeps_its_panel_and_its_input_across_switches()
    {
        CountingDraft.Built = 0;
        var draft = new WindowDefinition { Key = "draft", Title = "Draft", Icon = "", ComponentType = typeof(CountingDraft) };

        var manager = Manager(columns: 1);
        var window = manager.Open(draft);
        var desk = RenderComponent<DeskShell>();
        Win(desk, window).QuerySelector(".draft")!.Click();
        Assert.True(window.HasUnsavedInput);

        var health = desk.InvokeAsync(() => manager.Open(Window(SampleCatalog.Health))).Result;
        Assert.Contains("win--behind", Win(desk, window).ClassList);
        Assert.NotNull(Win(desk, window).QuerySelector(".draft"));

        Key(desk, health).Click();
        Win(desk, health).QuerySelectorAll(".menu__item")[1].Click();
        desk.InvokeAsync(() => manager.Focus(health.Id));
        desk.InvokeAsync(() => manager.Focus(window.Id));

        Assert.Equal(1, CountingDraft.Built);
        Assert.True(window.HasUnsavedInput);
        Assert.True(manager.IsShowing(window));
    }
}
