using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// An open Board row and the snippet it hosts under it. A filter that dropped the open row and
/// then brought it back left the old snippet hanging under a fresh strip that showed nothing
/// chosen, and no key could close it. Both ends now let go: the Board closes a row its filter
/// drops, and a strip that goes away takes back the snippet it handed out.
/// </summary>
public class BoardOpenRowTests : DeskTestContext
{
    // The grid's rows, as DeskGridTests (in the Desk's own tests) draws them: three, by id and name.
    public record Row(int Id, string Name);

    private static readonly Row[] Rows = [new(1, "One"), new(2, "Two"), new(3, "Three")];

    private static IReadOnlyList<Row> AllRows => Rows;

    private static IReadOnlyList<Row> Row2Hidden => [Rows[0], Rows[2]];

    private static BoardRow Board(int gameId)
    {
        var none = new MarketPair("h2h", null, null);
        return new BoardRow(new Game { Id = gameId }, none, none, none, 0, null, false, null);
    }

    [Fact]
    public void A_filter_that_drops_the_open_row_closes_it()
        => Assert.Null(BoardPanel.StillOpen(Board(2), [Board(1), Board(3)]));

    [Fact]
    public void A_reload_that_keeps_the_open_game_keeps_it_open_on_the_new_row()
    {
        var reloaded = Board(2);

        Assert.Same(reloaded, BoardPanel.StillOpen(Board(2), [Board(1), reloaded]));
    }

    [Fact]
    public void Nothing_open_stays_nothing_open()
        => Assert.Null(BoardPanel.StillOpen(null, [Board(1)]));

    /// <summary>
    /// The grid, the strip and the host wired as the Board wires them, minus the Board's own
    /// filter rule, so the strip's half is what is under test: filtered out, the strip goes
    /// and takes its snippet back; filtered in again, nothing hangs under the row.
    /// </summary>
    [Fact]
    public async Task A_row_filtered_out_and_back_leaves_no_snippet_under_it()
    {
        var host = RenderComponent<Host>();

        host.Find(".rowacts__keys button").Click();
        Assert.Contains("odds snippet", host.Find(".deskgrid__child").TextContent);

        await host.InvokeAsync(() => host.Instance.Show(Row2Hidden));
        Assert.Empty(host.FindAll(".deskgrid__child"));
        Assert.Null(host.Instance.Snippet);

        await host.InvokeAsync(() => host.Instance.Show(AllRows));
        Assert.Empty(host.FindAll(".deskgrid__child"));
        Assert.Equal("false", host.Find(".rowacts__keys button").GetAttribute("aria-pressed"));
    }

    private sealed class Host : ComponentBase
    {
        private static readonly IReadOnlyList<RowAction> Actions =
        [
            new()
            {
                Key = "odds",
                Label = "Odds",
                Icon = Icons.Material.Filled.History,
                Snippet = b => b.AddMarkupContent(0, "<p>odds snippet</p>")
            }
        ];

        private IReadOnlyList<Row> _visible = AllRows;
        private const int Open = 2;

        public RenderFragment? Snippet { get; private set; }

        public void Show(IReadOnlyList<Row> rows)
        {
            _visible = rows;
            StateHasChanged();
        }

        private void Hand(int id, RenderFragment? snippet)
        {
            if (id == Open)
                Snippet = snippet;
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<DeskGrid<Row>>(0);
            b.AddAttribute(1, "Items", _visible);
            b.AddAttribute(2, "ChildRow", Snippet is null
                ? null
                : (Func<Row, RenderFragment?>)(r => r.Id == Open ? Snippet : null));
            b.AddAttribute(3, "ChildContent", (RenderFragment)(columns =>
            {
                columns.OpenComponent<TemplateColumn<Row>>(0);
                columns.AddAttribute(1, "CellTemplate", (RenderFragment<CellContext<Row>>)(ctx => cell =>
                {
                    cell.AddContent(0, ctx.Item.Name);
                    if (ctx.Item.Id != Open)
                        return;

                    cell.OpenComponent<RowActions>(1);
                    cell.SetKey(ctx.Item.Id);
                    cell.AddAttribute(2, "Actions", Actions);
                    cell.AddAttribute(3, "SnippetHost",
                        EventCallback.Factory.Create<RenderFragment?>(this, s => Hand(ctx.Item.Id, s)));
                    cell.CloseComponent();
                }));
                columns.CloseComponent();
            }));
            b.CloseComponent();
        }
    }
}
