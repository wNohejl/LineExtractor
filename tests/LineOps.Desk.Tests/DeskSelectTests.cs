using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// The select: a field that picks from a list too long, or too unknown in advance, for a gate.
/// The options are the caller's markup; the field round them is the same box as every other
/// field — a real label bound by id when there is one, an error beside it, and no wrapper at
/// all when there is neither.
/// </summary>
public class DeskSelectTests : DeskTestContext
{
    private static readonly RenderFragment Sizes = b =>
    {
        foreach (var (value, text) in new[] { (1, "Small"), (2, "Medium"), (3, "Large") })
        {
            b.OpenElement(0, "option");
            b.AddAttribute(1, "value", value);
            b.AddContent(2, text);
            b.CloseElement();
        }
    };

    private IRenderedComponent<DeskSelect<int>> Select(int value, Action<int>? changed = null, string? error = null)
        => RenderComponent<DeskSelect<int>>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Label, "Size")
            .Add(x => x.Error, error)
            .Add(x => x.ValueChanged, (int v) => changed?.Invoke(v))
            .Add(x => x.ChildContent, Sizes));

    [Fact]
    public void Renders_the_callers_options()
    {
        var options = Select(2).FindAll("select option");

        Assert.Equal(["Small", "Medium", "Large"], options.Select(o => o.TextContent));
    }

    [Fact]
    public void The_label_names_the_select_by_id()
    {
        var cut = Select(2);

        var label = cut.Find("label.field-lbl");
        var select = cut.Find("select");

        Assert.Equal("Size", label.TextContent.Trim());
        Assert.Equal(select.Id, label.GetAttribute("for"));
        Assert.Contains("field", select.ClassList);
    }

    [Fact]
    public void The_value_reaches_the_select()
    {
        Assert.Equal("2", Select(2).Find("select").GetAttribute("value"));
    }

    [Fact]
    public void A_change_reports_the_typed_value()
    {
        var seen = new List<int>();

        Select(2, seen.Add).Find("select").Change("3");

        Assert.Equal([3], seen);
    }

    [Fact]
    public void An_error_renders_beside_the_select_and_describes_it()
    {
        var cut = Select(2, error: "Pick a size");

        var error = cut.Find("p.field-err");
        var select = cut.Find("select");

        Assert.Equal("Pick a size", error.TextContent);
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal(error.Id, select.GetAttribute("aria-describedby"));
        Assert.Equal("true", select.GetAttribute("aria-invalid"));
    }

    /// <summary>The frame rule: no label and no error is a bare select, so it sits in its row unmoved.</summary>
    [Fact]
    public void Without_a_label_or_an_error_the_select_renders_bare()
    {
        var cut = RenderComponent<DeskSelect<int>>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.ChildContent, Sizes)
            .AddUnmatched("aria-label", "Size")
            .AddUnmatched("style", "max-width:120px"));

        Assert.Empty(cut.FindAll(".field-set"));

        var select = cut.Find("select");
        Assert.Equal("Size", select.GetAttribute("aria-label"));
        Assert.Equal("max-width:120px", select.GetAttribute("style"));
    }

    [Fact]
    public void Required_marks_the_select_and_the_label()
    {
        var cut = RenderComponent<DeskSelect<int>>(p => p
            .Add(x => x.Label, "Size")
            .Add(x => x.Required, true)
            .Add(x => x.ChildContent, Sizes));

        Assert.True(cut.Find("select").HasAttribute("required"));
        Assert.Single(cut.FindAll(".field-req"));
    }
}
