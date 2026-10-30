using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The text area: a field with room for a paragraph. The same box, label and error as the
/// single-line field, so the two stack without a seam; it commits when the reader leaves it,
/// because prose is where checking every keystroke is least welcome.
/// </summary>
public class DeskTextAreaTests : DeskTestContext
{
    private IRenderedComponent<DeskTextArea> Area(string value = "A first draft", Action<string>? changed = null, string? error = null)
        => RenderComponent<DeskTextArea>(p => p
            .Add(x => x.Label, "Note")
            .Add(x => x.Value, value)
            .Add(x => x.Error, error)
            .Add(x => x.ValueChanged, (string v) => changed?.Invoke(v))
            .AddUnmatched("rows", 4));

    [Fact]
    public void The_value_reaches_the_text_area()
    {
        Assert.Equal("A first draft", Area().Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void A_change_reports_the_new_text()
    {
        var seen = new List<string>();

        Area(changed: seen.Add).Find("textarea").Change("A second draft");

        Assert.Equal(["A second draft"], seen);
    }

    /// <summary>Commit on leaving, not per keystroke: there is no input handler to fire mid-sentence.</summary>
    [Fact]
    public void It_commits_on_change_not_on_every_keystroke()
    {
        var textarea = Area(changed: _ => { }).Find("textarea");

        Assert.True(textarea.HasAttribute("blazor:onchange"));
        Assert.False(textarea.HasAttribute("blazor:oninput"));
    }

    [Fact]
    public void Rows_pass_through_to_the_text_area()
    {
        Assert.Equal("4", Area().Find("textarea").GetAttribute("rows"));
    }

    [Fact]
    public void The_label_names_the_text_area_by_id()
    {
        var cut = Area();

        Assert.Equal(cut.Find("textarea").Id, cut.Find("label.field-lbl").GetAttribute("for"));
    }

    [Fact]
    public void An_error_renders_the_error_line_and_describes_the_field()
    {
        var cut = Area(error: "Say what happened");

        var error = cut.Find("p.field-err");
        var textarea = cut.Find("textarea");

        Assert.Equal("Say what happened", error.TextContent);
        Assert.Equal(error.Id, textarea.GetAttribute("aria-describedby"));
        Assert.Equal("true", textarea.GetAttribute("aria-invalid"));
    }

    [Fact]
    public void No_error_renders_no_error_line()
    {
        Assert.Empty(Area().FindAll(".field-err"));
    }
}
