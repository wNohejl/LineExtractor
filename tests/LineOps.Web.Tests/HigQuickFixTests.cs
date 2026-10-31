using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The HIG review's quick fixes outside the key-row windows, pinned on the markup that carries
/// them. Source scans rather than renders: the snippets and subject windows read through
/// concrete EF services, and what is under test here is the shape of the markup — where type is
/// sized, what a loading snippet draws, what a door is made of — not the data behind it.
/// </summary>
public class HigQuickFixTests
{
    private static string Web(params string[] path)
        => File.ReadAllText(Path.Combine([Repo.Root(), "src", "LineOps.Web", "Components", .. path]));

    private static IEnumerable<(string Name, string Source)> Snippets()
        => Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Snippets"), "*.razor")
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    /// <summary>
    /// HIG 4: type has a scale, and a snippet's notes are on it through the Desk's
    /// <c>.snippet__note</c> rather than a pixel size typed into a <c>style</c>.
    /// </summary>
    [Fact]
    public void No_snippet_sizes_type_inline()
    {
        foreach (var (name, source) in Snippets())
            Assert.False(
                System.Text.RegularExpressions.Regex.IsMatch(source, @"style=""[^""]*font-size:"),
                $"{name} sizes type in a style attribute");
    }

    /// <summary>
    /// HIG 5: a snippet loads as the shape of what it will show, not a sentence about reading.
    /// The sentence was the only text in a box sized for three rows, and it jumped when they came.
    /// </summary>
    [Theory]
    [InlineData("FormSnippet", "3")]
    [InlineData("HeadToHeadSnippet", "3")]
    [InlineData("OddsSnippet", "2")]
    [InlineData("PlayerRecentSnippet", "3")]
    public void No_snippet_loads_with_a_sentence(string snippet, string lines)
    {
        var source = Web("Snippets", snippet + ".razor");

        Assert.DoesNotContain("EmptyState Style=\"padding:6px 0\">Reading", source);
        Assert.Contains($"<DeskSkeleton Lines=\"{lines}\" />", source);
    }

    /// <summary>HIG 2, in the Game window: its price door is a button, with no keydown of its own.</summary>
    [Fact]
    public void The_game_window_s_price_door_is_a_button()
    {
        var source = Web("Panels", "GamePanel.razor");

        Assert.Contains("<button type=\"button\" class=\"gamelines__cell gamelines__cell--take\"", source);
        Assert.DoesNotContain("role=\"@(CanTake(offer)", source);
        Assert.DoesNotContain("@onkeydown=\"@(e => { if (e.Key is \"Enter\" or \" \") Take(", source);
    }

    /// <summary>
    /// HIG 7: a switch keeps every position on the panel, which is right up to about five; past
    /// that the season is a select.
    /// </summary>
    [Theory]
    [InlineData("TeamPanel")]
    [InlineData("PlayerPanel")]
    public void A_long_season_history_is_a_select(string panel)
    {
        var source = Web("Panels", panel + ".razor");

        Assert.Contains("_seasonOptions.Count > 5", source);
        Assert.Contains("<DeskSelect TValue=\"int\" aria-label=\"Season\"", source);
    }

    /// <summary>HIG 21: the copy says what it means.</summary>
    [Theory]
    [InlineData("Panels/TeamPanel.razor", "\"ATS ✗\"")]
    [InlineData("Panels/TeamPanel.razor", "\"ATS P\"")]
    [InlineData("Panels/HeadToHeadPanel.razor", "\"No\"")]
    [InlineData("Panels/HeadToHeadPanel.razor", "\"Cover\"")]
    [InlineData("Snippets/HeadToHeadSnippet.razor", "\"No\"")]
    [InlineData("Snippets/HeadToHeadSnippet.razor", "\"Cover\"")]
    [InlineData("Snippets/OddsSnippet.razor", "best of @_row")]
    [InlineData("Panels/RunsPanel.razor", "Title=\"Started (ET)\"")]
    public void The_copy_says_what_it_means(string file, string retired)
        => Assert.DoesNotContain(retired, Web(file.Split('/')));
}
