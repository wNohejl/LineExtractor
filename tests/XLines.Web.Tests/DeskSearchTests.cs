using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace XLines.Web.Tests;

/// <summary>
/// The search field: a search landmark around a searchbox in the desk's field well, with the
/// magnifier leading, a clear key only when there is something to clear, and Escape clearing.
/// </summary>
public class DeskSearchTests : DeskTestContext
{
    private IRenderedComponent<DeskSearch> Search(string value, List<string>? seen = null, bool immediate = true)
        => RenderComponent<DeskSearch>(p => p
            .Add(x => x.Placeholder, "Search players")
            .Add(x => x.Value, value)
            .Add(x => x.Immediate, immediate)
            .Add(x => x.ValueChanged, (string s) => seen?.Add(s)));

    [Fact]
    public void Renders_a_searchbox_inside_a_search_landmark()
    {
        var cut = Search("");

        var landmark = cut.Find("[role=search]");
        Assert.Contains("search", landmark.ClassList);

        var input = landmark.QuerySelector("input")!;
        Assert.Equal("search", input.GetAttribute("type"));
        Assert.Equal("searchbox", input.GetAttribute("role"));
        Assert.Equal("Search players", input.GetAttribute("placeholder"));
        Assert.Equal("Search players", input.GetAttribute("aria-label"));
        // The same well as every other field on the desk.
        Assert.Contains("field", input.ClassList);

        // The magnifier leads, and is decoration.
        var glyph = landmark.FirstElementChild!;
        Assert.Contains("search__glyph", glyph.ClassList);
        Assert.Equal("true", glyph.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void The_clear_key_shows_only_with_text()
    {
        Assert.Empty(Search("").FindAll(".search__clear"));

        var clear = Search("judge").Find("button.search__clear");
        Assert.Equal("Clear search", clear.GetAttribute("aria-label"));
        Assert.Equal("button", clear.GetAttribute("type"));
    }

    [Fact]
    public void Typing_commits_on_every_keystroke_when_immediate()
    {
        var seen = new List<string>();
        var cut = Search("", seen);

        cut.Find("input").Input("ju");

        Assert.Equal(["ju"], seen);
        Assert.NotEmpty(cut.FindAll(".search__clear"));
    }

    [Fact]
    public void Typing_commits_on_change_when_not_immediate()
    {
        var seen = new List<string>();
        var cut = Search("", seen, immediate: false);

        cut.Find("input").Input("ju");
        Assert.Empty(seen);

        cut.Find("input").Change("judge");
        Assert.Equal(["judge"], seen);
    }

    [Fact]
    public void The_clear_key_empties_the_field()
    {
        var seen = new List<string>();
        var cut = Search("judge", seen);

        cut.Find(".search__clear").Click();

        Assert.Equal([""], seen);
        Assert.Empty(cut.FindAll(".search__clear"));
        Assert.Equal("", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Escape_clears_and_other_keys_do_not()
    {
        var seen = new List<string>();
        var cut = Search("judge", seen);

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "a" });
        Assert.Empty(seen);

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal([""], seen);
        Assert.Empty(cut.FindAll(".search__clear"));
    }

    [Fact]
    public void Escape_on_an_empty_field_raises_nothing()
    {
        var seen = new List<string>();
        var cut = Search("", seen);

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(seen);
    }

    [Fact]
    public void Class_and_unmatched_attributes_pass_through()
    {
        var cut = RenderComponent<DeskSearch>(p => p
            .Add(x => x.Placeholder, "Search players")
            .Add(x => x.Class, "extra")
            .AddUnmatched("data-test", "q1"));

        var landmark = cut.Find("[role=search]");
        Assert.Contains("extra", landmark.ClassList);
        Assert.Equal("q1", landmark.GetAttribute("data-test"));
    }

    // ---- The search in a window's toolbar ----------------------------------------------------

    private static string Sheet(params string[] path) => File.ReadAllText(Path.Combine([Repo.Root(), "src", .. path]));

    private static readonly string Panels = Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Panels");

    /// <summary>
    /// A toolbar's search shares the row with the keys: the field and the actions are siblings in
    /// the one row, the search straight before them, not a full-width field on a row of its own
    /// that pushes the window's content down.
    /// </summary>
    [Fact]
    public void In_a_toolbar_the_search_shares_the_row_with_the_keys()
    {
        var cut = RenderComponent<PanelToolbar>(p => p
            .AddChildContent("<select aria-label=\"Sport\"></select>")
            .Add(x => x.Search, b =>
            {
                b.OpenComponent<DeskSearch>(0);
                b.AddAttribute(1, nameof(DeskSearch.Placeholder), "Search teams");
                b.AddAttribute(2, nameof(DeskSearch.Label), "Search teams or matchups");
                b.AddAttribute(3, nameof(DeskSearch.Value), "mets");
                b.CloseComponent();
            })
            .Add(x => x.Actions, "<button>Refresh</button>"));

        var row = cut.Find(".ptoolbar").Children;
        var search = row.Single(c => c.ClassList.Contains("search"));
        var actions = row.Single(c => c.ClassList.Contains("ptoolbar__actions"));

        Assert.Same(actions, search.NextElementSibling);
        // Named for what it searches, and the field can be emptied in one press.
        Assert.Equal("Search teams or matchups", search.QuerySelector("input")!.GetAttribute("aria-label"));
        Assert.Equal("Search teams", search.QuerySelector("input")!.GetAttribute("placeholder"));
        Assert.NotNull(search.QuerySelector("button.search__clear[aria-label='Clear search']"));
    }

    /// <summary>
    /// The toolbar's search is a compact trailing control: it never grows into the row's free
    /// space (it once stretched across a maximised Board between the pickers and the keys), it
    /// leads the trailing group with an auto margin, and the keys then follow it at the row's gap.
    /// Its width is read from the type ramp, so it is the Desk's to set and no window restyles it.
    /// </summary>
    [Fact]
    public void A_toolbar_search_takes_the_room_a_query_needs_and_no_more()
    {
        var controls = DeskConventions.WithoutComments(Sheet("XLines.Desk", "wwwroot", "css", "controls.css"));

        var rule = Regex.Match(controls, @"(?m)^\.ptoolbar > \.search \{(?<body>[^}]*)\}");
        Assert.True(rule.Success, "controls.css draws the toolbar's search");

        var flex = Regex.Match(rule.Groups["body"].Value, @"flex:\s*(?<grow>\d+)\s+\d+\s+(?<basis>[^;]+);");
        Assert.True(flex.Success, "The toolbar's search states its flex");
        Assert.Equal("0", flex.Groups["grow"].Value);
        Assert.Contains("var(--text-body)", flex.Groups["basis"].Value);
        Assert.Matches(@"margin-left:\s*auto", rule.Groups["body"].Value);
        Assert.Matches(@"\.ptoolbar > \.search ~ \.ptoolbar__actions \{\s*margin-left:\s*0", controls);

        // No window widens it again.
        var app = DeskConventions.WithoutComments(Sheet("XLines.Web", "wwwroot", "css", "xlines.css"));
        Assert.DoesNotMatch(@"\.ptoolbar[^{},]*\.search\b", app);
    }

    /// <summary>
    /// Every search in a window is a DeskSearch in a toolbar's Search slot — the same side and
    /// order in every window — except the game finder, which heads the wager form it fills.
    /// Each placeholder says what is searched, as a verb and an object.
    /// </summary>
    [Fact]
    public void Every_window_search_sits_in_its_toolbar_and_says_what_it_searches()
    {
        foreach (var file in Directory.EnumerateFiles(Panels, "*.razor"))
        {
            var source = File.ReadAllText(file);

            foreach (Match search in Regex.Matches(source, @"<DeskSearch\b[^>]*>"))
            {
                var name = Path.GetFileName(file);
                Assert.Matches(@"Placeholder=""Search \w+", search.Value);

                if (name == "GamePicker.razor")
                    continue;

                var slot = source.LastIndexOf("<Search>", search.Index, StringComparison.Ordinal);
                var closed = source.LastIndexOf("</Search>", search.Index, StringComparison.Ordinal);
                Assert.True(slot >= 0 && slot > closed, $"{name}: a DeskSearch outside a PanelToolbar's <Search> slot");
            }
        }
    }

    /// <summary>The roster finder searches with the search field, not a plain one.</summary>
    [Fact]
    public void Players_search_is_a_search_field()
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Panels", "PlayersPanel.razor"));

        Assert.Contains("<DeskSearch", source);
        Assert.Contains("Placeholder=\"Search players", source);
        Assert.DoesNotContain("<DeskField", source);
    }

    /// <summary>
    /// A search that heads a list is a combobox: it names the list, says whether it is open,
    /// announces the option the arrows rest on while focus stays in the field, and is where
    /// the popover returns focus when the list goes.
    /// </summary>
    [Fact]
    public void A_search_that_heads_a_list_is_a_combobox()
    {
        var cut = RenderComponent<DeskSearch>(p => p
            .Add(x => x.Placeholder, "Find a game")
            .Add(x => x.Value, "ste")
            .Add(x => x.ListId, "games-1")
            .Add(x => x.Expanded, true)
            .Add(x => x.ActiveDescendant, "games-1-2"));

        var input = cut.Find("input");
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("list", input.GetAttribute("aria-autocomplete"));
        Assert.Equal("true", input.GetAttribute("aria-expanded"));
        Assert.Equal("games-1", input.GetAttribute("aria-controls"));
        Assert.Equal("games-1-2", input.GetAttribute("aria-activedescendant"));
        Assert.Equal("true", input.GetAttribute("data-popover-return"));
    }

    [Fact]
    public void A_plain_search_is_a_searchbox_and_names_no_list()
    {
        var input = Search("ste").Find("input");

        Assert.Equal("searchbox", input.GetAttribute("role"));
        Assert.Null(input.GetAttribute("aria-expanded"));
        Assert.Null(input.GetAttribute("aria-controls"));
        Assert.Null(input.GetAttribute("data-popover-return"));
    }

    /// <summary>With the list open, Escape is the owner's to close it; the text stays for the next Escape.</summary>
    [Fact]
    public void Escape_with_the_list_open_reaches_the_owner_and_keeps_the_text()
    {
        var seen = new List<string>();
        var keys = new List<string>();
        var cut = RenderComponent<DeskSearch>(p => p
            .Add(x => x.Placeholder, "Find a game")
            .Add(x => x.Value, "ste")
            .Add(x => x.ValueChanged, (string s) => seen.Add(s))
            .Add(x => x.ListId, "games-1")
            .Add(x => x.Expanded, true)
            .Add(x => x.OnKeyDown, (KeyboardEventArgs e) => keys.Add(e.Key)));

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(["Escape"], keys);
        Assert.Empty(seen);
    }

    [Fact]
    public void Escape_with_the_list_closed_clears_the_text()
    {
        var seen = new List<string>();
        var cut = RenderComponent<DeskSearch>(p => p
            .Add(x => x.Placeholder, "Find a game")
            .Add(x => x.Value, "ste")
            .Add(x => x.ValueChanged, (string s) => seen.Add(s))
            .Add(x => x.ListId, "games-1")
            .Add(x => x.Expanded, false));

        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal([""], seen);
    }
}
