using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests;

/// <summary>A floating dialog closes on Escape pressed inside it, and on nothing else.</summary>
public class DeskDialogTests : DeskTestContext
{
    [Fact]
    public void Escape_inside_the_dialog_closes_it()
    {
        var closed = 0;
        var cut = RenderComponent<DeskDialog>(p => p
            .Add(x => x.Title, "Every book")
            .Add(x => x.OnClose, () => closed++));

        cut.Find(".deskdialog").KeyDown("Escape");

        Assert.Equal(1, closed);
    }

    [Fact]
    public void Another_key_does_not()
    {
        var closed = 0;
        var cut = RenderComponent<DeskDialog>(p => p
            .Add(x => x.Title, "Every book")
            .Add(x => x.OnClose, () => closed++));

        cut.Find(".deskdialog").KeyDown("Enter");

        Assert.Equal(0, closed);
    }
}
