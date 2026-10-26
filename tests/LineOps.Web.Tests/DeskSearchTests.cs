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
}
