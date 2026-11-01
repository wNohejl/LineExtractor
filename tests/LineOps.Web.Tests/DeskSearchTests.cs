using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

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

    /// <summary>The roster finder searches with the search field, not a plain one.</summary>
    [Fact]
    public void Players_search_is_a_search_field()
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "PlayersPanel.razor"));

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
