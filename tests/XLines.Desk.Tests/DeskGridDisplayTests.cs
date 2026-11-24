using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace XLines.Desk.Tests;

/// <summary>
/// How a grid's columns read: the heading row's type, the room a sort arrow keeps, one line per
/// cell, and the row states. The look lives in mud-bridge.css and is checked in the browser; what
/// can be held here is that the hooks those rules hang from are drawn, and that the rules keep the
/// measures and the specificity they were written to.
/// </summary>
public class DeskGridDisplayTests : DeskTestContext
{
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two")];

    private static string Bridge =>
        DeskConventions.WithoutComments(File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "mud-bridge.css")));

    // The declarations of the first rule whose selector list is exactly this, however it is broken
    // across lines.
    private static string Rule(string selector)
    {
        var pattern = string.Join(@"\s+", selector.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        var match = Regex.Match(Bridge, @"(?:^|[}\s])" + pattern + @"\s*\{([^}]*)\}", RegexOptions.Multiline);
        Assert.True(match.Success, $"No rule for {selector} in mud-bridge.css.");
        return match.Groups[1].Value;
    }

    private IRenderedComponent<DeskGrid<Row>> Grid(bool fixedHeader = false)
        => RenderComponent<DeskGrid<Row>>(p => p
            .Add(x => x.Items, Rows)
            .Add(x => x.FixedHeader, fixedHeader)
            .Add(x => x.ChildContent, b =>
            {
                b.OpenComponent<PropertyColumn<Row, int>>(0);
                b.AddAttribute(1, "Property", (Expression<Func<Row, int>>)(r => r.Id));
                b.AddAttribute(2, "Title", "Count");
                b.AddAttribute(3, "Align", "Align.Right");
                b.CloseComponent();

                b.OpenComponent<PropertyColumn<Row, string>>(4);
                b.AddAttribute(5, "Property", (Expression<Func<Row, string>>)(r => r.Name));
                b.AddAttribute(6, "Title", "Name");
                b.AddAttribute(7, "Sortable", false);
                b.CloseComponent();
            }));

    // A sortable heading reserves room for its arrow, found by the arrow it draws; a heading that
    // does not sort must draw none, or it would reserve room for nothing.
    [Fact]
    public void Only_a_sortable_heading_draws_the_arrow_its_room_is_reserved_for()
    {
        var headings = Grid().FindAll("thead th");

        Assert.NotNull(headings[0].QuerySelector(".sort-direction-icon"));
        Assert.Null(headings[1].QuerySelector(".sort-direction-icon"));
    }

    [Fact]
    public void A_fixed_header_carries_the_class_the_sticky_band_answers()
    {
        Assert.Empty(Grid().FindAll(".mud-table-sticky-header"));
        Assert.NotEmpty(Grid(fixedHeader: true).FindAll(".deskgrid .mud-table-sticky-header .mud-table-head th"));
    }

    // The reserve is the arrow's footprint: its box and the gap between it and the word. If either
    // changes alone, the arrow either reaches the next column again or a gap opens beside every word.
    [Fact]
    public void The_reserve_beside_a_heading_is_the_arrow_s_own_width()
    {
        var reserve = int.Parse(Regex.Match(Rule(".deskgrid"), @"--deskgrid-sort:\s*(\d+)px").Groups[1].Value);
        var arrow = int.Parse(Regex.Match(
            Rule(".deskgrid .mud-table-head .mud-table-cell .column-header .sort-direction-icon"), @"(?<![-\w])width:\s*(\d+)px").Groups[1].Value);
        var gap = int.Parse(Regex.Match(
            Rule(".deskgrid .mud-table-head .mud-table-cell .column-header .column-options"), @"margin-left:\s*(\d+)px").Groups[1].Value);

        Assert.Equal(arrow + gap, reserve);
    }

    [Fact]
    public void The_heading_row_is_set_from_the_type_ramp_in_sentence_case()
    {
        var heading = Rule(".deskgrid .mud-table-head .mud-table-cell");

        Assert.Contains("font-size: var(--text-subheadline)", heading);
        Assert.Contains("font-weight: var(--weight-semibold)", heading);
        Assert.Contains("color: var(--text-secondary)", heading);
        Assert.Contains("text-transform: none", heading);
        Assert.Contains("letter-spacing: normal", heading);
        Assert.DoesNotContain("uppercase", Bridge[Bridge.IndexOf(".deskgrid", StringComparison.Ordinal)..Bridge.IndexOf(".mud-dialog", StringComparison.Ordinal)]);
    }

    // One line per cell, so the rows of one grid share one height.
    [Fact]
    public void A_clamped_cell_holds_to_one_line()
    {
        var clamp = Rule(".deskgrid .mud-table-body .mud-table-cell .desklink__label, .deskgrid .mud-table-body .mud-table-cell .trunc:not(.mud-table-cell)");

        Assert.Contains("-webkit-line-clamp: 1", clamp);
        Assert.Contains("line-clamp: 1", clamp);
    }

    // An application lights its open row at four classes and loads after the bridge; the hover on a
    // pressable row is written so that it never counts more, or an open row would go grey under
    // the pointer.
    [Fact]
    public void A_pressable_row_s_hover_stays_below_an_application_s_open_row()
        => Assert.Contains(".deskgrid--clickable :where(.mud-table-body) > .mud-table-row:hover > .mud-table-cell", Bridge);

    [Fact]
    public void The_bridge_wins_without_important()
        => Assert.DoesNotContain("!important", Bridge);
}
