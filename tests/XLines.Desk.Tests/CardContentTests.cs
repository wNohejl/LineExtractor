using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace XLines.Desk.Tests;

/// <summary>
/// What a card holds that is not a row — a table, a lone sentence, an inline confirm — is set by
/// the Desk once, against the markup DeskGrid and EmptyState actually render, rather than by a copy
/// in every window's scoped sheet: a table's text starts at the bar title's inset and its last
/// hairline stands down for the card's edge; a lone sentence and a confirm take the rows' inset.
/// </summary>
public class CardContentTests : DeskTestContext
{
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two"), new(3, "Three")];

    private static string Css(string name) => DeskConventions.WithoutComments(
        File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", name)));

    private const string TableInset = ".wgroup > .deskgrid";
    private const string LastHairline = ".wgroup .deskgrid .mud-table-body .mud-table-row:last-child > .mud-table-cell";
    private const string LoneSentence = ".wgroup .empty:not(.wgroup__row .empty)";
    private const string ConfirmRow = ".wgroup > .confirm";

    private IRenderedComponent<FormGroup> CardWithTable() => RenderComponent<FormGroup>(p => p
        .Add(x => x.Title, "Recent")
        .Add(x => x.ChildContent, b =>
        {
            b.OpenComponent<DeskGrid<Row>>(0);
            b.AddAttribute(1, "Items", (IReadOnlyList<Row>)Rows);
            b.AddAttribute(2, "ChildContent", (RenderFragment)(c =>
            {
                c.OpenComponent<PropertyColumn<Row, int>>(0);
                c.AddAttribute(1, "Property", (Expression<Func<Row, int>>)(r => r.Id));
                c.CloseComponent();
                c.OpenComponent<PropertyColumn<Row, string>>(2);
                c.AddAttribute(3, "Property", (Expression<Func<Row, string>>)(r => r.Name));
                c.CloseComponent();
            }));
            b.CloseComponent();
        }));

    [Fact]
    public void The_table_rules_reach_the_grid_a_card_renders_and_only_its_last_row()
    {
        var cut = CardWithTable();

        // The grid is the card's own content, so the inset rule reaches it.
        Assert.Single(cut.FindAll(TableInset));

        // The hairline that stands down is the last body row's, every cell of it, and no other.
        var rows = cut.FindAll(".deskgrid .mud-table-body .mud-table-row");
        Assert.Equal(Rows.Length, rows.Count);
        var cleared = cut.FindAll(LastHairline);
        Assert.NotEmpty(cleared);
        Assert.All(cleared, cell => Assert.Same(rows[^1], cell.ParentElement));
        Assert.Equal(rows[^1].Children.Length, cleared.Count);
    }

    [Fact]
    public void A_lone_sentence_is_the_card_s_but_one_inside_a_row_is_the_row_s()
    {
        var cut = RenderComponent<FormGroup>(p => p
            .Add(x => x.Title, "Timeline")
            .Add(x => x.ChildContent, b =>
            {
                b.OpenComponent<EmptyState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(c => c.AddContent(0, "Nothing yet.")));
                b.CloseComponent();
                b.OpenComponent<FormRow>(2);
                b.AddAttribute(3, "Label", "Source");
                b.AddAttribute(4, "ChildContent", (RenderFragment)(c =>
                {
                    c.OpenComponent<EmptyState>(0);
                    c.AddAttribute(1, "ChildContent", (RenderFragment)(d => d.AddContent(0, "None registered.")));
                    c.CloseComponent();
                }));
                b.CloseComponent();
            }));

        // EmptyState renders the .empty paragraph the rule names; the one in a row is excluded by it.
        var sentences = cut.FindAll(".wgroup .empty");
        Assert.Equal(2, sentences.Count);
        var lone = sentences.Where(s => s.Closest(".wgroup__row") is null).ToList();
        Assert.Equal("Nothing yet.", Assert.Single(lone).TextContent.Trim());
    }

    [Fact]
    public void The_rules_are_the_desk_s_once_and_outweigh_the_seam()
    {
        var desk = Css("desk.css");

        Assert.Contains(TableInset + " { padding-inline: calc(var(--space-4) - var(--deskgrid-inset)); }", desk);
        Assert.Contains(LastHairline + " { border-bottom: 0; }", desk);
        Assert.Contains(LoneSentence + " { margin: 0; padding: var(--space-3) var(--space-4); }", desk);
        Assert.Contains(ConfirmRow + " { padding: var(--space-2) var(--space-4); }", desk);

        // mud-bridge.css loads after desk.css, so the hairline rule wins by weight, not by order:
        // it must outweigh the seam's body-cell rule that draws the hairline.
        Assert.Contains(".deskgrid .mud-table-body .mud-table-cell {", Css("mud-bridge.css"));
        Assert.True(Classes(LastHairline) > Classes(".deskgrid .mud-table-body .mud-table-cell"));

        // The narrow card gives the same three their --space-3 inset back, inside its query.
        var narrow = Regex.Match(desk, @"@container \(max-width: 480px\) \{(?<body>(?:[^{}]|\{[^{}]*\})*)\}");
        Assert.True(narrow.Success);
        foreach (var selector in new[] { TableInset, LoneSentence, ConfirmRow })
            Assert.Contains(selector + " {", narrow.Groups["body"].Value);
    }

    /// <summary>The weight a selector carries in classes and pseudo-classes (no ids are used).</summary>
    private static int Classes(string selector) => Regex.Matches(selector, @"\.[\w-]+|:(?!not)[\w-]+").Count;
}
