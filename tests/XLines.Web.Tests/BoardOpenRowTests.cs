using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Desk.Primitives;
using XLines.Web.Components.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

namespace XLines.Web.Tests;

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

        Glances(host)[0].Click();
        Assert.Contains("odds snippet", host.Find(".deskgrid__child").TextContent);

        await host.InvokeAsync(() => host.Instance.Show(Row2Hidden));
        Assert.Empty(host.FindAll(".deskgrid__child"));
        Assert.Null(host.Instance.Snippet);

        // Back, the row's own line holds a fresh strip with nothing chosen and nothing under it.
        await host.InvokeAsync(() => host.Instance.Show(AllRows));
        Assert.Empty(host.FindAll(".deskgrid__child [role=region]"));
        Assert.All(Glances(host), g => Assert.Equal("false", g.GetAttribute("aria-checked")));
    }

    /// <summary>
    /// The strip has a row of its own. Wedged under the team chips in the matchup cell it wrapped
    /// into three stacked lines of keys, and a press a few pixels off a key landed on the game row
    /// and closed it. In the child row it is one line, first, with the answer beneath it.
    /// </summary>
    [Fact]
    public void The_open_rows_strip_is_the_first_line_of_its_child_row()
    {
        var host = RenderComponent<Host>();

        var cell = host.Find(".deskgrid__child .deskgrid__child-cell");
        Assert.Contains("rowacts", cell.FirstElementChild!.ClassList);
        Assert.Empty(host.FindAll("tbody tr.mud-table-row .rowacts"));

        // The answer a glance opens draws beneath the keys, in the same cell.
        Glances(host)[1].Click();
        cell = host.Find(".deskgrid__child .deskgrid__child-cell");
        Assert.Equal(2, cell.Children.Length);
        Assert.Contains("rowacts__snippet", cell.Children[1].ClassList);
        Assert.Contains("h2h snippet", cell.Children[1].TextContent);
        Assert.NotNull(host.Instance.Snippet);
    }

    [Fact]
    public void A_press_anywhere_in_the_child_row_leaves_the_row_open()
    {
        var host = RenderComponent<Host>();
        Glances(host)[0].Click();

        // The padding round the strip and the lead cell under the start time: the near misses.
        // (A press on the strip's or the snippet's own box stops there, before any handler, which
        // bUnit reports as having nowhere to go; DeskGridTests holds the child row's boundary.)
        host.Find(".deskgrid__child-lead").Click();
        host.Find(".deskgrid__child-cell").Click();
        host.Find(".rowacts__keys button:not([role=radio])").Click();

        Assert.Equal(2, host.Instance.Open);
        Assert.Equal(1, host.Instance.Commands);
        Assert.Contains("odds snippet", host.Find(".deskgrid__child").TextContent);
    }

    [Fact]
    public void The_game_rows_own_click_still_closes_it_and_takes_the_snippet_back()
    {
        var host = RenderComponent<Host>();
        Glances(host)[0].Click();

        host.FindAll("tbody tr.mud-table-row")[1].Children[0].Click();

        Assert.Null(host.Instance.Open);
        Assert.Empty(host.FindAll(".deskgrid__child"));
        Assert.Null(host.Instance.Snippet);
    }

    /// <summary>
    /// A command is the host's callback, not a delegate run by the strip, so the host is drawn
    /// after it: the Board's Place wager key opened its take and nothing redrew the board until a
    /// refresh or a live score did, and the key read as dead.
    /// </summary>
    [Fact]
    public void A_command_that_changes_its_host_is_drawn_by_the_host()
    {
        var host = RenderComponent<Host>();
        Assert.Equal("0", host.Find(".cmds").TextContent);

        host.Find(".rowacts__keys button:not([role=radio])").Click();

        Assert.Equal("1", host.Find(".cmds").TextContent);
    }

    [Fact]
    public void Every_key_on_the_strip_is_reachable_from_the_keyboard()
    {
        var host = RenderComponent<Host>();

        // One tab stop into the glances (a segmented control roves inside itself), and every
        // command its own.
        Assert.Single(Glances(host), g => g.GetAttribute("tabindex") == "0");
        Assert.All(host.FindAll(".rowacts__keys button:not([role=radio])"), key =>
        {
            Assert.NotEqual("-1", key.GetAttribute("tabindex"));
            Assert.False(key.HasAttribute("disabled"));
        });
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Glances(IRenderedComponent<Host> host)
        => host.FindAll(".deskgrid__child .rowacts__keys [role=radio]");

    /// <summary>
    /// The Board's wiring, without the Board: a grid whose row click toggles one open row, and the
    /// open row's child row drawing the strip and, beneath it, the snippet the strip hands over.
    /// </summary>
    private sealed class Host : ComponentBase
    {
        private IReadOnlyList<RowAction> Actions =>
        [
            new()
            {
                Key = "odds",
                Label = "Odds",
                Icon = Icons.Material.Filled.History,
                Snippet = b => b.AddMarkupContent(0, "<p>odds snippet</p>")
            },
            new()
            {
                Key = "h2h",
                Label = "H2H",
                Icon = Icons.Material.Filled.History,
                Snippet = b => b.AddMarkupContent(0, "<p>h2h snippet</p>")
            },
            new()
            {
                Key = "bets",
                Label = "More bets",
                Icon = Icons.Material.Filled.ViewList,
                OpenWindow = EventCallback.Factory.Create(this, () => Commands++)
            }
        ];

        private IReadOnlyList<Row> _visible = AllRows;

        public int? Open { get; private set; } = 2;

        public int Commands { get; private set; }

        public RenderFragment? Snippet { get; private set; }

        public void Show(IReadOnlyList<Row> rows)
        {
            _visible = rows;
            StateHasChanged();
        }

        private void Toggle(Row row)
        {
            Open = Open == row.Id ? null : row.Id;
            Snippet = null;
        }

        private void Hand(int id, RenderFragment? snippet)
        {
            if (id == Open)
                Snippet = snippet;
        }

        private RenderFragment OpenRow(Row row) => b =>
        {
            b.OpenComponent<RowActions>(0);
            b.SetKey(row.Id);
            b.AddAttribute(1, "Actions", Actions);
            b.AddAttribute(2, "SnippetHost",
                EventCallback.Factory.Create<RenderFragment?>(this, s => Hand(row.Id, s)));
            b.CloseComponent();
            b.AddContent(3, Snippet);
        };

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<DeskGrid<Row>>(0);
            b.AddAttribute(1, "Items", _visible);
            b.AddAttribute(2, "OnRowClick",
                EventCallback.Factory.Create<DataGridRowClickEventArgs<Row>>(this, e => Toggle(e.Item)));
            b.AddAttribute(3, "ChildRow", (Func<Row, RenderFragment?>)(r => r.Id == Open ? OpenRow(r) : null));
            b.AddAttribute(4, "ChildRowIndent", 1);
            b.AddAttribute(5, "ChildContent", (RenderFragment)(columns =>
            {
                columns.OpenComponent<PropertyColumn<Row, int>>(0);
                columns.AddAttribute(1, "Property", (System.Linq.Expressions.Expression<Func<Row, int>>)(r => r.Id));
                columns.CloseComponent();
                columns.OpenComponent<TemplateColumn<Row>>(2);
                columns.AddAttribute(3, "CellTemplate", (RenderFragment<CellContext<Row>>)(ctx => cell =>
                    cell.AddContent(0, ctx.Item.Name)));
                columns.CloseComponent();
            }));
            b.CloseComponent();

            // What a command changed, drawn by this host: the test reads it back without asking
            // the host to redraw.
            b.OpenElement(6, "p");
            b.AddAttribute(7, "class", "cmds");
            b.AddContent(8, Commands);
            b.CloseElement();
        }
    }
}
