using Bunit;
using LineOps.Web.Components.Desk;

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
}
