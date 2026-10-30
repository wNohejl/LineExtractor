using System.Linq.Expressions;
using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// A row's child row: what the Board opens under a game so its snippet reads across the row
/// rather than inside the matchup column it was opened from.
/// </summary>
public class DeskGridTests : DeskTestContext
{
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two"), new(3, "Three")];

    internal static IReadOnlyList<Row> AllRows => Rows;

    internal static IReadOnlyList<Row> Row2Hidden => [Rows[0], Rows[2]];

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

/// <summary>
/// What a grid draws around its rows: the heading row, its sort arrow, a column's alignment,
/// and the line an empty grid shows instead of a table.
/// </summary>
public class DeskGridHeadingTests : DeskTestContext
{
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two")];

    // An Id column on the trailing edge, as a panel asks for one, ahead of a plain Name column.
    // Razor hands Align over as the text it was written as; a builder may pass the enum itself.
    private static RenderFragment Columns(object? align = null, bool sortedByName = false) => b =>
    {
        b.OpenComponent<PropertyColumn<Row, int>>(0);
        b.AddAttribute(1, "Property", (Expression<Func<Row, int>>)(r => r.Id));
        b.AddAttribute(2, "Title", "Starts");
        if (align is not null)
            b.AddAttribute(3, "Align", align);
        b.CloseComponent();

        b.OpenComponent<PropertyColumn<Row, string>>(4);
        b.AddAttribute(5, "Property", (Expression<Func<Row, string>>)(r => r.Name));
        b.AddAttribute(6, "Title", "Matchup");
        if (sortedByName)
            b.AddAttribute(7, "InitialDirection", SortDirection.Descending);
        b.CloseComponent();
    };

    private IRenderedComponent<DeskGrid<Row>> Grid(IEnumerable<Row> rows, RenderFragment columns, bool multiSort = false, bool loading = false)
        => RenderComponent<DeskGrid<Row>>(p => p
            .Add(x => x.Items, rows)
            .Add(x => x.ChildContent, columns)
            .Add(x => x.MultiSort, multiSort)
            .Add(x => x.Loading, loading)
            .Add(x => x.EmptyText, "No open alerts."));

    [Theory]
    [InlineData("Align.Right")]
    [InlineData(Align.Right)]
    [InlineData(Align.End)]
    public void A_right_aligned_column_sets_its_heading_and_its_cells_on_the_same_edge(object align)
    {
        var cut = Grid(Rows, Columns(align));

        // MudDataGrid's columns take no Align and draw nothing for it; the grid answers it, on the
        // heading and every cell alike, so a label can never sit apart from its figures.
        cut.WaitForAssertion(() => Assert.Contains("deskgrid__end", cut.FindAll("thead th")[0].ClassList));
        Assert.All(cut.FindAll("tbody tr"), row => Assert.Contains("deskgrid__end", row.Children[0].ClassList));
    }

    [Fact]
    public void A_column_that_asks_for_no_alignment_takes_none()
    {
        var cut = Grid(Rows, Columns(align: "Align.Right"));

        cut.WaitForAssertion(() => Assert.Contains("deskgrid__end", cut.FindAll("thead th")[0].ClassList));
        Assert.DoesNotContain("deskgrid__end", cut.FindAll("thead th")[1].ClassList);
        Assert.All(cut.FindAll("tbody tr"), row => Assert.DoesNotContain("deskgrid__end", row.Children[1].ClassList));
    }

    // The grid learns its columns only by drawing them, so it draws once more to align them (and
    // to give a wide grid its handles). That second render is the whole of it: a parent that
    // renders again with the same rows costs one render, not another alignment pass. Ceilings
    // rather than exact counts, so a MudBlazor that renders less does not fail them.
    //
    // Counted at the grid's own render tree, not with bUnit's RenderCount: that counted the mount
    // as one render although the grid drew twice, so it could not tell a second pass from none.
    [Fact]
    public void The_alignment_pass_renders_once_more_and_then_no_more()
    {
        var cut = RenderComponent<CountingGrid>(p => p
            .Add(x => x.Items, Rows)
            .Add(x => x.ChildContent, Columns(align: "Align.Right")));

        cut.WaitForAssertion(() => Assert.Contains("deskgrid__end", cut.FindAll("thead th")[0].ClassList));
        var mounted = cut.Instance.Renders;
        Assert.True(mounted <= 2, $"Mounting the grid took {mounted} renders; the alignment pass should add one.");

        cut.SetParametersAndRender(p => p.Add(x => x.Items, Rows));

        var again = cut.Instance.Renders - mounted;
        Assert.True(again <= 1, $"Setting the same rows again took {again} renders; the columns were already aligned.");
        Assert.Contains("deskgrid__end", cut.FindAll("thead th")[0].ClassList);
    }

    // The grid as it is, counting each time it draws.
    public class CountingGrid : DeskGrid<Row>
    {
        public int Renders { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Renders++;
            base.BuildRenderTree(builder);
        }
    }

    [Fact]
    public void A_heading_reads_as_its_label_and_nothing_else()
    {
        var cut = Grid(Rows, Columns(sortedByName: true));

        // MudBlazor's multiple mode numbered every heading, a hidden "0" on each unsorted one and a
        // "1" beside the sorted one's arrow, and the heading's text came out as "Starts0".
        var headings = cut.FindAll("thead th").Select(th => th.TextContent.Trim()).ToList();

        Assert.Equal(new[] { "Starts", "Matchup" }, headings);
        Assert.Empty(cut.FindAll(".mud-sort-index"));
    }

    [Fact]
    public void Only_the_sorted_column_carries_a_direction()
    {
        var cut = Grid(Rows, Columns(sortedByName: true));

        var arrows = cut.FindAll("thead th .sort-direction-icon");

        Assert.Equal(2, arrows.Count);
        Assert.DoesNotContain(arrows[0].ClassList, c => c.StartsWith("mud-direction-"));
        Assert.Contains("mud-direction-desc", arrows[1].ClassList);
    }

    [Fact]
    public void A_grid_that_asks_for_multi_sort_numbers_its_sort_keys()
        => Assert.NotEmpty(Grid(Rows, Columns(sortedByName: true), multiSort: true).FindAll(".mud-sort-index"));

    [Fact]
    public void An_empty_grid_draws_its_sentence_and_no_heading_row()
    {
        var cut = Grid([], Columns());

        Assert.Equal("No open alerts.", cut.Find(".deskgrid__empty").TextContent.Trim());

        // The table stays in the tree so a sort survives a filter that empties it, but it is not
        // drawn, and Mud's own bold, centred no-records cell is not drawn at all.
        Assert.True(cut.Find(".mud-data-grid").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll(".mud-table-empty-row"));
    }

    [Fact]
    public void A_grid_with_rows_draws_no_empty_line()
    {
        var cut = Grid(Rows, Columns());

        Assert.Empty(cut.FindAll(".deskgrid__empty"));
        Assert.False(cut.Find(".mud-data-grid").HasAttribute("hidden"));
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void An_empty_first_load_shows_placeholders_rather_than_the_empty_line()
    {
        var cut = Grid([], Columns(), loading: true);

        Assert.NotNull(cut.Find(".deskgrid__skeleton"));
        Assert.Empty(cut.FindAll(".deskgrid__empty"));
    }
}
