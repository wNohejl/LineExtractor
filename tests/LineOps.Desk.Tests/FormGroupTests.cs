using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The grouped form, as one spelling: a sentence-case head above, one filled group with one
/// edge, rows divided inside it, and the explaining sentence under it as the footer. The rows
/// lead with their label in the group's shared column.
/// </summary>
public class FormGroupTests : DeskTestContext
{
    private IRenderedComponent<FormGroup> Group(string? title = "Delivery", bool footer = true, string? labelWidth = null)
        => RenderComponent<FormGroup>(p =>
        {
            p.Add(x => x.Title, title)
             .Add(x => x.LabelWidth, labelWidth)
             .AddChildContent<FormRow>(r => r.Add(x => x.Label, "Carrier").AddChildContent("<span class=\"val\">Post</span>"))
             .AddChildContent<FormRow>(r => r.Add(x => x.Label, "Quantity").AddChildContent("<input aria-label=\"Quantity\" />"));

            if (footer)
                p.Add(x => x.Footer, "Arrives in two days.");
        });

    [Fact]
    public void A_group_is_a_named_section_with_one_group_and_its_footer_under_it()
    {
        var cut = Group();

        var section = cut.Find("section.wsection");
        var head = section.QuerySelector("h3.wgroup__head")!;
        Assert.Equal("Delivery", head.TextContent);
        Assert.Equal(head.Id, section.GetAttribute("aria-labelledby"));

        // One group, the footer after it and outside it.
        var group = section.QuerySelector(".wgroup")!;
        Assert.Single(section.QuerySelectorAll(".wgroup"));
        var foot = section.QuerySelector("p.wgroup__foot")!;
        Assert.Equal("Arrives in two days.", foot.TextContent);
        Assert.Null(group.QuerySelector(".wgroup__foot"));
        Assert.True(group.CompareDocumentPosition(foot).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public void Rows_lead_with_their_label_in_the_form_row()
    {
        var rows = Group().FindAll(".wgroup > .wgroup__row");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Contains("wgroup__row--form", r.ClassList));
        Assert.Equal("Carrier", rows[0].FirstElementChild!.TextContent);
        Assert.Contains("wgroup__label", rows[0].FirstElementChild!.ClassList);
        Assert.NotNull(rows[1].QuerySelector("input[aria-label=Quantity]"));
    }

    [Fact]
    public void A_group_without_a_title_or_footer_draws_neither()
    {
        var cut = Group(title: null, footer: false);

        Assert.Empty(cut.FindAll(".wgroup__head"));
        Assert.Empty(cut.FindAll(".wgroup__foot"));
        Assert.Null(cut.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void The_label_column_is_the_group_s_to_set()
    {
        var group = Group(labelWidth: "120px").Find(".wgroup");

        Assert.Contains("--form-label: 120px", group.GetAttribute("style"));
    }
}

/// <summary>
/// A row's label can carry markup, and a row's leading column can be something other than a
/// label; both stay FormRows, so the group's shared column and its narrow stacking hold.
/// </summary>
public class FormRowSlotTests : DeskTestContext
{
    [Fact]
    public void Label_content_is_drawn_as_the_row_s_label()
    {
        var cut = RenderComponent<FormRow>(p => p
            .Add(x => x.LabelContent, b => b.AddMarkupContent(0, "Price<span class=\"echo\">-110</span>"))
            .AddChildContent("<input aria-label=\"Price\" />"));

        var label = cut.Find(".wgroup__row--form > .wgroup__label");
        Assert.Equal("Price-110", label.TextContent);
        Assert.NotNull(label.QuerySelector(".echo"));
    }

    [Fact]
    public void A_lead_takes_the_column_in_place_of_the_label()
    {
        var cut = RenderComponent<FormRow>(p => p
            .Add(x => x.Lead, b => b.AddMarkupContent(0, "<span class=\"wait\">Choose one above.</span>"))
            .AddChildContent("<button type=\"button\" disabled>Book</button>"));

        var row = cut.Find(".wgroup__row--form");
        Assert.Empty(row.QuerySelectorAll(".wgroup__label"));
        Assert.Equal("wait", row.FirstElementChild!.ClassName);
    }
}
