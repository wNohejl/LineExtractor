using Bunit;
using LineOps.Web.Components.Desk;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Board's value filter. It is an on/off, so it is a checkbox — not a key that swaps
/// between Plain and Tinted to say which state it is in. It reads and writes nothing, so a
/// visitor has it too (ADR 0018 hides controls that write).
/// </summary>
public class BoardToolbarTests : DeskTestContext
{
    [Fact]
    public void Ev_filter_is_a_checkbox()
    {
        bool? changed = null;

        var cut = RenderComponent<BoardValueFilter>(p => p
            .Add(x => x.Count, 3)
            .Add(x => x.Checked, false)
            .Add(x => x.CheckedChanged, (bool on) => changed = on));

        var box = cut.Find("label.checkbox input[type=checkbox]");

        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Only +EV (3)", cut.Find(".checkbox__label").TextContent.Trim());
        Assert.Empty(cut.FindAll("button"));

        box.Change(true);

        Assert.True(changed);
    }

    [Fact]
    public void Ev_filter_is_absent_when_no_game_has_value()
    {
        var cut = RenderComponent<BoardValueFilter>(p => p
            .Add(x => x.Count, 0)
            .Add(x => x.Checked, false));

        // Nothing at all, not an empty wrapper: an empty element in a toolbar is still a gap.
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    private static string BoardSource()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "BoardPanel.razor"));

    /// <summary>The slice of a source between a slot's opening and closing tags.</summary>
    internal static string Slot(string source, string name)
    {
        var start = source.IndexOf($"<{name}>", StringComparison.Ordinal);
        var end = source.IndexOf($"</{name}>", StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"No <{name}> slot");
        return source[start..end];
    }

    [Fact]
    public void Board_search_is_a_search_field()
    {
        var source = BoardSource();
        var search = Slot(source, "Search");

        Assert.Contains("<DeskSearch", search);
        Assert.Contains("Placeholder=\"Search teams\"", search);
        Assert.DoesNotContain("placeholder=\"Team\"", source);
    }

    [Fact]
    public void Board_filters_live_in_overflow()
    {
        // At a narrow width the scope pickers and the value filter fold into More; the search
        // and the keys stay on the row until the row is narrower still.
        var source = BoardSource();
        var overflow = Slot(source, "Overflow");

        Assert.Contains("_sportOptions", overflow);
        Assert.Contains("Options=\"Horizons\"", overflow);
        Assert.Contains("Options=\"_phaseOptions\"", overflow);
        Assert.Contains("<BoardValueFilter", overflow);
        Assert.DoesNotContain("<DeskSearch", overflow);
        Assert.DoesNotContain("DeskJobMenu", overflow);

        // Each folded control carries the label it shows as a row of the More panel.
        Assert.Contains("class=\"ptoolbar__label\" aria-hidden=\"true\">Sport<", overflow);
    }
}
