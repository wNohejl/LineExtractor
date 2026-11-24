using Bunit;
using XLines.Desk.Primitives;

namespace XLines.Desk.Tests;

/// <summary>
/// The grouped form, as one spelling, drawn as a section card: one filled group with one edge,
/// its name inside it in a header bar with an optional key trailing, rows divided inside it, and
/// the explaining sentence under the card as the footer. The rows lead with their label in the
/// group's shared column.
/// </summary>
public class FormGroupTests : DeskTestContext
{
    private IRenderedComponent<FormGroup> Group(
        string? title = "Delivery",
        bool footer = true,
        string? labelWidth = null,
        string? keys = null,
        DeskCardSpan span = DeskCardSpan.Track,
        string? notice = null)
        => RenderComponent<FormGroup>(p =>
        {
            p.Add(x => x.Title, title)
             .Add(x => x.LabelWidth, labelWidth)
             .Add(x => x.Span, span)
             .AddChildContent<FormRow>(r => r.Add(x => x.Label, "Carrier").AddChildContent("<span class=\"val\">Post</span>"))
             .AddChildContent<FormRow>(r => r.Add(x => x.Label, "Quantity").AddChildContent("<input aria-label=\"Quantity\" />"));

            if (footer)
                p.Add(x => x.Footer, "Arrives in two days.");
            if (keys is not null)
                p.Add(x => x.Keys, b => b.AddMarkupContent(0, keys));
            if (notice is not null)
                p.Add(x => x.Notice, b => b.AddMarkupContent(0, notice));
        });

    [Fact]
    public void A_group_is_a_card_named_in_its_bar_with_its_footer_under_it()
    {
        var cut = Group();

        var section = cut.Find("section.wsection");

        // One card; its name is inside it, in the bar that opens it, and names the section.
        var group = Assert.Single(section.QuerySelectorAll(":scope > .wgroup"));
        var bar = group.FirstElementChild!;
        Assert.Contains("wgroup__bar", bar.ClassList);
        var title = bar.QuerySelector("h3.wgroup__title")!;
        Assert.Equal("Delivery", title.TextContent);
        Assert.Equal(title.Id, section.GetAttribute("aria-labelledby"));

        // No name floating outside the card any more.
        Assert.Empty(section.QuerySelectorAll(".wgroup__head"));

        // The footer after the card and outside it.
        var foot = section.QuerySelector(":scope > p.wgroup__foot")!;
        Assert.Equal("Arrives in two days.", foot.TextContent);
        Assert.Null(group.QuerySelector(".wgroup__foot"));
        Assert.True(group.CompareDocumentPosition(foot).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public void Rows_lead_with_their_label_in_the_form_row_after_the_bar()
    {
        var rows = Group().FindAll(".wgroup > .wgroup__row");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Contains("wgroup__row--form", r.ClassList));
        Assert.Equal("Carrier", rows[0].FirstElementChild!.TextContent);
        Assert.Contains("wgroup__label", rows[0].FirstElementChild!.ClassList);
        Assert.NotNull(rows[1].QuerySelector("input[aria-label=Quantity]"));

        // The bar is the card's head, not one of its rows, so it never takes a row's hairline.
        Assert.Contains("wgroup__bar", rows[0].PreviousElementSibling!.ClassName);
        Assert.DoesNotContain("wgroup__row", rows[0].PreviousElementSibling!.ClassName);
    }

    [Fact]
    public void Keys_trail_the_title_in_the_bar()
    {
        var bar = Group(keys: "<span class=\"count\">3 parcels</span>").Find(".wgroup__bar");

        var keys = bar.QuerySelector(":scope > .wgroup__keys")!;
        Assert.Equal("3 parcels", keys.TextContent);
        Assert.True(bar.QuerySelector(".wgroup__title")!.CompareDocumentPosition(keys).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public void A_bar_with_only_keys_still_opens_the_card()
    {
        var cut = Group(title: null, keys: "<button type=\"button\">Add</button>");

        var bar = cut.Find(".wgroup > .wgroup__bar");
        Assert.Empty(bar.QuerySelectorAll(".wgroup__title"));
        Assert.NotNull(bar.QuerySelector(".wgroup__keys > button"));
        Assert.Null(cut.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void A_group_without_a_title_keys_or_footer_draws_none_of_them()
    {
        var cut = Group(title: null, footer: false);

        Assert.Empty(cut.FindAll(".wgroup__bar"));
        Assert.Empty(cut.FindAll(".wgroup__title"));
        Assert.Empty(cut.FindAll(".wgroup__foot"));
        Assert.Null(cut.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void The_label_column_is_the_group_s_to_set()
    {
        var group = Group(labelWidth: "120px").Find(".wgroup");

        Assert.Contains("--form-label: 120px", group.GetAttribute("style"));
    }

    [Theory]
    [InlineData(DeskCardSpan.Track, null)]
    [InlineData(DeskCardSpan.Row, "wsection--span")]
    [InlineData(DeskCardSpan.Rest, "wsection--rest")]
    public void A_card_says_how_much_of_the_flow_s_row_it_takes(DeskCardSpan span, string? modifier)
    {
        var section = Group(span: span).Find("section.wsection");

        var modifiers = section.ClassList.Where(c => c.StartsWith("wsection--", StringComparison.Ordinal)).ToList();
        if (modifier is null)
            Assert.Empty(modifiers);
        else
            Assert.Equal(modifier, Assert.Single(modifiers));
    }

    /// <summary>A note is a box of its own, so it sits under the card, before the footer — never inside it.</summary>
    [Fact]
    public void A_notice_sits_between_the_card_and_its_footer()
    {
        var section = Group(notice: "<div class=\"note note--warn\">Over the limit.</div>").Find("section.wsection");

        var children = section.Children.Select(c => c.ClassName).ToList();
        Assert.Equal(["wgroup", "note note--warn", "wgroup__foot"], children.Select(c => c!.Trim()));
        Assert.Empty(section.QuerySelectorAll(".wgroup .note"));
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

/// <summary>
/// The card rules the sheet holds: one hairline per card and none nested, the bar's hairline full
/// width and its fill drawn from the ramp's own tokens, the flow's tracks capped rather than
/// stretched, and the split row stacking from the card's own width.
/// </summary>
public class SectionCardCssTests
{
    private static string Css => DeskConventions.WithoutComments(
        File.ReadAllText(Path.Combine(Support.Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css")));

    private static string Rule(string selector)
    {
        var match = System.Text.RegularExpressions.Regex.Match(Css, @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{[^}]*\}");
        Assert.True(match.Success, $"{selector} is not declared");
        return match.Value;
    }

    [Fact]
    public void The_bar_is_divided_from_the_rows_by_one_full_width_hairline_and_draws_no_edge_of_its_own()
    {
        var bar = Rule(".wgroup__bar");

        Assert.Contains("border-bottom: 1px solid var(--separator)", bar);
        Assert.DoesNotContain("border:", bar);
        Assert.DoesNotContain("border-radius", bar);
        Assert.Matches(@"background: color-mix\(in srgb, var\(--surface-2\) \d+%, var\(--surface-1\)\)", bar);
    }

    [Fact]
    public void The_title_is_the_headline_style_in_the_primary_ink()
    {
        var title = Rule(".wgroup__title");

        Assert.Contains("font-size: var(--text-headline)", title);
        Assert.Contains("font-weight: var(--weight-semibold)", title);
        Assert.Contains("color: var(--text-primary)", title);
    }

    [Fact]
    public void The_flow_adds_columns_rather_than_widening_cards_and_aligns_them_to_the_start()
    {
        var flow = Rule(".wflow");

        Assert.Contains("grid-template-columns: repeat(auto-fill, minmax(min(100%, 420px), 1fr))", flow);
        Assert.Contains("align-items: start", flow);
        Assert.Contains("container-name: wflow", flow);
        Assert.Contains("grid-column: 1 / -1", Rule(".wflow > .wsection--span"));
    }

    [Fact]
    public void Nothing_inside_a_card_draws_a_box()
    {
        foreach (var selector in new[] { ".wgroup__cell", ".wgroup__keys", ".wgroup__title" })
        {
            var rule = Rule(selector);
            Assert.DoesNotContain("border:", rule);
            Assert.DoesNotContain("box-shadow", rule);
        }
    }
}
