using System.Linq.Expressions;
using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// A row's child row: what the Board opens under a game so its snippet reads across the row
/// rather than inside the matchup column it was opened from.
/// </summary>
public class DeskGridTests : DeskTestContext
{
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two"), new(3, "Three")];

    private static RenderFragment Columns => b =>
    {
        b.OpenComponent<PropertyColumn<Row, int>>(0);
        b.AddAttribute(1, "Property", (Expression<Func<Row, int>>)(r => r.Id));
        b.CloseComponent();
        b.OpenComponent<PropertyColumn<Row, string>>(2);
        b.AddAttribute(3, "Property", (Expression<Func<Row, string>>)(r => r.Name));
        b.CloseComponent();
    };

    private static RenderFragment Text(string text) => b => b.AddMarkupContent(0, $"<p>{text}</p>");

    private IRenderedComponent<DeskGrid<Row>> Grid(Func<Row, RenderFragment?>? childRow, int indent = 0)
        => RenderComponent<DeskGrid<Row>>(p => p
            .Add(x => x.Items, Rows)
            .Add(x => x.ChildContent, Columns)
            .Add(x => x.ChildRow, childRow)
            .Add(x => x.ChildRowIndent, indent));

    [Fact]
    public void A_grid_without_a_child_row_draws_only_its_rows()
    {
        var cut = Grid(null);

        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        Assert.Empty(cut.FindAll(".deskgrid__child"));
    }

    [Fact]
    public void A_child_row_renders_under_its_own_row_only()
    {
        var cut = Grid(r => r.Id == 2 ? Text("under two") : null);

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(4, rows.Count);

        // Directly beneath the row it belongs to, and nowhere else: a row with no child row
        // draws no empty one, so a closed board keeps its rhythm.
        Assert.Contains("deskgrid__child", rows[2].ClassList);
        Assert.Contains("Two", rows[1].TextContent);
        Assert.Contains("under two", rows[2].TextContent);
        Assert.Single(cut.FindAll(".deskgrid__child"));
    }

    [Fact]
    public void A_child_row_spans_the_row_from_the_indented_column()
    {
        var cut = Grid(r => r.Id == 1 ? Text("under one") : null, indent: 1);

        var cells = cut.FindAll(".deskgrid__child > td");

        // The lead cell sits under the first column, so the content's leading edge is the
        // second column's — the Board's matchup — whatever width the first takes.
        Assert.Equal(2, cells.Count);
        Assert.Equal("1", cells[0].GetAttribute("colspan"));
        Assert.Empty(cells[0].TextContent.Trim());
        Assert.Contains("deskgrid__child-cell", cells[1].ClassList);
        Assert.Contains("under one", cells[1].TextContent);
        Assert.True(int.Parse(cells[1].GetAttribute("colspan")!) >= 2);
    }

    [Fact]
    public void A_child_row_with_no_indent_starts_at_the_first_column()
    {
        var cut = Grid(r => r.Id == 3 ? Text("under three") : null);

        var cells = cut.FindAll(".deskgrid__child > td");

        Assert.Single(cells);
        Assert.Contains("under three", cells[0].TextContent);
    }
}
