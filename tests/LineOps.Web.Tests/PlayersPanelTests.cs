using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The roster finder, read from the source because it renders against a database. Its search
/// field is bound to the search text, not handed the field's name as a literal, and the Pos
/// column stands down when nothing on screen has a position (ESPN's NFL box scores name none).
/// </summary>
public class PlayersPanelTests
{
    private static string Source() => File.ReadAllText(Path.Combine(
        Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "PlayersPanel.razor"));

    /// <summary>
    /// DeskSearch's Value is a plain string parameter, so <c>Value="_search"</c> passed the text
    /// "_search", and the empty field showed it.
    /// </summary>
    [Fact]
    public void The_search_field_is_bound_to_the_search_text()
    {
        var source = Source();

        Assert.Contains("<DeskSearch Placeholder=\"Search players\"", source);
        Assert.Contains("Value=\"@_search\"", source);
        Assert.DoesNotContain("Value=\"_search\" ValueChanged", source);
    }

    /// <summary>No column of dashes: the column hides while no row has a position, and a row without one is blank.</summary>
    [Fact]
    public void Pos_shows_only_when_a_row_has_a_position()
    {
        var source = Source();

        Assert.Contains("Title=\"Pos\" Hidden=\"@(!_anyPosition)\"", source);
        Assert.Contains("_anyPosition = _rows.Any(r => !string.IsNullOrWhiteSpace(r.Player.Position));", source);
        Assert.DoesNotContain("Position ?? \"—\"", source);
    }
}
