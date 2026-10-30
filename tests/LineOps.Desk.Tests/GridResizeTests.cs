using System.Linq.Expressions;
using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// Wide grids let their columns be dragged wider (HIG lists and tables, macOS: resizable
/// columns). A narrow one does not: three columns already fit, and a handle on each heading is
/// one more thing to catch by accident.
/// </summary>
public class GridResizeTests : DeskTestContext
{
    public record Row(int A, int B, int C, int D, int E, int F);

    private static readonly Row[] Rows = [new(1, 2, 3, 4, 5, 6)];

    private static RenderFragment Columns(int count) => b =>
    {
        Expression<Func<Row, int>>[] all = [r => r.A, r => r.B, r => r.C, r => r.D, r => r.E, r => r.F];

        for (var i = 0; i < count; i++)
        {
            b.OpenComponent<PropertyColumn<Row, int>>(0);
            b.AddAttribute(1, "Property", all[i]);
            b.CloseComponent();
        }
    };

    private IRenderedComponent<DeskGrid<Row>> Grid(int columns, bool? resizable = null)
        => RenderComponent<DeskGrid<Row>>(p =>
        {
            p.Add(x => x.Items, Rows).Add(x => x.ChildContent, Columns(columns));

            if (resizable is { } r)
                p.Add(x => x.Resizable, r);
        });

    private static int Handles(IRenderedFragment cut) => cut.FindAll(".mud-resizer").Count;

    [Fact]
    public void A_grid_of_more_than_five_columns_resizes()
        => Assert.True(Handles(Grid(6)) > 0);

    [Fact]
    public void A_grid_of_five_or_fewer_does_not()
        => Assert.Equal(0, Handles(Grid(3)));

    [Fact]
    public void The_caller_can_say_either_way()
    {
        Assert.Equal(0, Handles(Grid(6, resizable: false)));
        Assert.True(Handles(Grid(3, resizable: true)) > 0);
    }
}
