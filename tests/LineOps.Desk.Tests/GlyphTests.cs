using Bunit;
using LineOps.Desk.Windowing;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// The bare icon: a MudBlazor icon string drawn as an <c>&lt;svg&gt;</c> with no wrapper, in the
/// text's colour, and hidden from screen readers and from the Tab order — a glyph decorates the
/// control it sits in, and that control is what carries the name.
/// </summary>
public class GlyphTests : DeskTestContext
{
    [Fact]
    public void Draws_the_icon_as_a_bare_svg()
    {
        var cut = RenderComponent<Glyph>(p => p.Add(x => x.Icon, Icons.Material.Filled.Add));

        var svg = cut.Find("svg");

        Assert.Single(cut.Nodes);
        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.Equal("currentColor", svg.GetAttribute("fill"));
        Assert.NotNull(svg.QuerySelector("path"));
    }

    [Fact]
    public void The_icon_s_own_path_reaches_the_svg()
    {
        const string icon = "<path d=\"M1 1h22v22H1z\"/>";

        var cut = RenderComponent<Glyph>(p => p.Add(x => x.Icon, icon));

        Assert.Equal("M1 1h22v22H1z", cut.Find("svg path").GetAttribute("d"));
    }

    [Fact]
    public void It_is_hidden_from_screen_readers_and_from_focus()
    {
        var svg = RenderComponent<Glyph>(p => p.Add(x => x.Icon, Icons.Material.Filled.Add)).Find("svg");

        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
    }
}
