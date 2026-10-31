using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace LineOps.Desk.Tests;

/// <summary>
/// A row of keys that reads one way everywhere: the secondary keys, then the sentence saying why
/// the primary waits, then the one Filled key. The sentence is text because a disabled key's
/// title never shows, and the primary is pointed at it, so a screen reader hears it too.
/// </summary>
public class KeyRowTests : DeskTestContext
{
    private static RenderFragment Secondary => b =>
    {
        b.OpenElement(0, "button");
        b.AddAttribute(1, "class", "second");
        b.AddContent(2, "Cancel");
        b.CloseElement();
    };

    private static RenderFragment<string?> Primary => described => b =>
    {
        b.OpenElement(0, "button");
        b.AddAttribute(1, "class", "first");
        b.AddAttribute(2, "aria-describedby", described);
        b.AddContent(3, "Save");
        b.CloseElement();
    };

    private IRenderedComponent<KeyRow> Row(string? reason, DeskJustify? justify = null) =>
        RenderComponent<KeyRow>(p =>
        {
            p.Add(x => x.Secondary, Secondary)
             .Add(x => x.Reason, reason)
             .Add(x => x.Primary, Primary);

            if (justify is { } j)
                p.Add(x => x.Justify, j);
        });

    [Fact]
    public void Secondary_then_reason_then_primary()
    {
        var cut = Row("Pick an owner first.");

        var row = cut.Find(".keyrow");
        Assert.Equal("group", row.GetAttribute("role"));

        var order = row.Children.Select(c => c.ClassName).ToList();
        Assert.Equal(["second", "keyrow__reason", "first"], order);
    }

    [Fact]
    public void The_reason_describes_the_primary()
    {
        var cut = Row("Pick an owner first.");

        var reason = cut.Find(".keyrow__reason");
        Assert.Equal("Pick an owner first.", reason.TextContent);
        Assert.False(string.IsNullOrEmpty(reason.Id));
        Assert.Equal(reason.Id, cut.Find(".first").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void No_reason_no_sentence_and_no_id()
    {
        var cut = Row(null);

        Assert.Empty(cut.FindAll(".keyrow__reason"));
        Assert.Empty(cut.FindAll("[id]"));
        Assert.Null(cut.Find(".first").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Two_rows_never_share_an_id()
    {
        var one = Row("Pick an owner first.");
        var two = Row("Pick an owner first.");

        Assert.NotEqual(one.Find(".keyrow__reason").Id, two.Find(".keyrow__reason").Id);
    }

    [Fact]
    public void The_id_holds_across_renders()
    {
        var cut = Row("Pick an owner first.");
        var before = cut.Find(".keyrow__reason").Id;

        cut.SetParametersAndRender(p => p.Add(x => x.Reason, "Name the amount first."));

        Assert.Equal(before, cut.Find(".keyrow__reason").Id);
    }

    [Fact]
    public void Justify_start_and_end()
    {
        Assert.Contains("keyrow--end", Row(null).Find(".keyrow").ClassList);
        Assert.Contains("keyrow--start", Row(null, DeskJustify.Start).Find(".keyrow").ClassList);
    }

    [Fact]
    public void A_class_is_appended_and_other_attributes_pass_through()
    {
        var cut = RenderComponent<KeyRow>(p => p
            .Add(x => x.Class, "mine")
            .AddUnmatched("data-x", "1"));

        var row = cut.Find(".keyrow");
        Assert.Contains("keyrow--end", row.ClassList);
        Assert.Contains("mine", row.ClassList);
        Assert.Equal("1", row.GetAttribute("data-x"));
    }
}
