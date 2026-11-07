using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Game window keeps up with its game without reloading itself (spec 2026-10-24 §5 items
/// 4–5). A score change in some other game is not this window's business; one in its own game
/// reloads the game's row and nothing else; the bets, the form, the rosters and the box score
/// wait for the journal, a bare notice, or the final whistle. And the title says where the game
/// stands — the clock and the down — not just that it is live.
/// </summary>
public class GamePanelLiveTests : DeskTestContext
{
    private readonly FakeGameWindowData _data = new();

    public GamePanelLiveTests() => Services.AddSingleton<IGameWindowData>(_data);

    private static Game Bears_at_Patriots(GameStatus status = GameStatus.Live)
    {
        var game = BoardRows.Game();
        game.HomeTeamId = 1;
        game.HomeTeam = new Team { Id = 1, Name = "New England Patriots", Abbrev = "NE" };
        game.AwayTeamId = 2;
        game.AwayTeam = new Team { Id = 2, Name = "Chicago Bears", Abbrev = "CHI" };
        game.Status = status;
        game.HomeScore = 17;
        game.AwayScore = 20;
        return game;
    }

    private IRenderedComponent<GamePanel> Render(string? windowId = null)
    {
        var cut = RenderComponent<GamePanel>(p =>
        {
            p.Add(g => g.GameId, BoardRows.GameId);
            if (windowId is not null)
                p.AddCascadingValue("WindowId", windowId);
        });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ptitle")));
        return cut;
    }

    // Past the panel's one-second settle, with room to spare: for asserting that nothing reloads.
    private static void Settle() => Thread.Sleep(1600);

    // How long a reload that should happen may take. A test that expects one waits for it rather
    // than sleeping a fixed time and looking once, which a slower CI runner can miss: the settle
    // is a second, and the read after it is not instant.
    private static readonly TimeSpan Reload = TimeSpan.FromSeconds(5);

    [Fact]
    public void A_change_to_another_game_does_not_reload_the_window()
    {
        Render();
        var (rows, games, details) = (_data.RowReads, _data.GameReads, _data.DetailReads);

        Signal("games", "4123");
        Signal("odds", "4123");
        Settle();

        Assert.Equal((rows, games, details), (_data.RowReads, _data.GameReads, _data.DetailReads));
    }

    [Fact]
    public void A_change_to_this_game_reloads_its_row_only()
    {
        _data.Row = BoardRows.Row() with { Game = Bears_at_Patriots() };
        var cut = Render();
        var (rows, details) = (_data.RowReads, _data.DetailReads);

        var moved = Bears_at_Patriots();
        moved.AwayScore = 27;
        _data.Game = moved;
        Signal("games", BoardRows.GameId.ToString());

        cut.WaitForAssertion(() => Assert.Equal(1, _data.GameReads), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Contains("27", cut.Find(".ptitle__name").TextContent));
        Assert.Equal(rows, _data.RowReads);
        Assert.Equal(details, _data.DetailReads);
    }

    [Fact]
    public void A_price_change_on_this_game_reloads_the_row_with_its_prices_but_not_the_rest()
    {
        var cut = Render();
        var (rows, details) = (_data.RowReads, _data.DetailReads);

        Signal("odds", BoardRows.GameId.ToString());

        cut.WaitForAssertion(() => Assert.Equal(rows + 1, _data.RowReads), Reload);
        Settle();
        Assert.Equal(rows + 1, _data.RowReads);
        Assert.Equal(details, _data.DetailReads);
    }

    [Theory]
    [InlineData("journal")]
    [InlineData("games")]
    public void The_journal_or_a_bare_notice_reloads_everything(string topic)
    {
        var cut = Render();
        var (rows, details) = (_data.RowReads, _data.DetailReads);

        Signal(topic);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(rows + 1, _data.RowReads);
            Assert.Equal(details + 1, _data.DetailReads);
        }, Reload);
    }

    [Fact]
    public void The_final_whistle_reloads_the_box_score_and_the_form()
    {
        _data.Row = BoardRows.Row() with { Game = Bears_at_Patriots() };
        var cut = Render();
        var details = _data.DetailReads;

        _data.Game = Bears_at_Patriots(GameStatus.Final);
        Signal("games", BoardRows.GameId.ToString());

        cut.WaitForAssertion(() => Assert.Equal(details + 1, _data.DetailReads), Reload);
    }

    /// <summary>
    /// The live pass lands the final first, with no box score yet; the results sweep writes the
    /// box hours later, addressed to this game. A final window without a box reads its detail
    /// again on that notice, or the box never appears until the window is reopened.
    /// </summary>
    [Fact]
    public void A_final_without_a_box_score_reloads_its_detail_when_its_game_moves()
    {
        _data.Row = BoardRows.Row() with { Game = Bears_at_Patriots(GameStatus.Final) };
        var cut = Render();
        var details = _data.DetailReads;

        var final = Bears_at_Patriots(GameStatus.Final);
        _data.Game = final;
        _data.Detail = GameWindowDetail.Empty with
        {
            Box = new GameBoxScore(final,
                [new BoxScoreLine(7, "Drake Maye", "QB", true, new Dictionary<string, string> { ["YDS"] = "281" })],
                [])
        };
        Signal("games", BoardRows.GameId.ToString());

        cut.WaitForAssertion(() => Assert.Equal(details + 1, _data.DetailReads), TimeSpan.FromSeconds(5));

        // With the box in hand, the next notice for this final is the row's alone again.
        var after = _data.DetailReads;
        Signal("games", BoardRows.GameId.ToString());
        Settle();

        Assert.Equal(after, _data.DetailReads);
    }

    [Fact]
    public void A_live_title_carries_the_clock_and_the_situation()
    {
        var game = Bears_at_Patriots();
        game.Period = 3;
        game.DisplayClock = "4:12";
        game.StatusDetail = "4:12 - 3rd";
        game.PossessionTeamId = 2;
        game.DownDistanceText = "2nd & 6 at NE 34";
        game.LastPlay = "D.Maye pass incomplete short left to S.Diggs.";
        _data.Row = BoardRows.Row() with { Game = game };

        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Game)!,
            new Dictionary<string, object> { ["GameId"] = BoardRows.GameId });
        var cut = Render(window.Id);

        Assert.Equal("Q3 4:12", cut.Find(".ptitle__row .tag").TextContent.Trim());
        var detail = cut.Find(".ptitle__text").TextContent;
        Assert.Contains("CHI ball, 2nd & 6 at NE 34", detail);
        Assert.Contains("Last: D.Maye pass incomplete short left to S.Diggs.", detail);

        // The rail chip says the same short status.
        cut.WaitForAssertion(() => Assert.StartsWith("Q3 4:12 · CHI 20–17 NE", window.StatusText));
    }

    [Fact]
    public void A_live_title_with_no_state_of_play_draws_none()
    {
        _data.Row = BoardRows.Row() with { Game = Bears_at_Patriots() };

        var cut = Render();

        Assert.Equal("Live", cut.Find(".ptitle__row .tag").TextContent.Trim());
        Assert.DoesNotContain("ball", cut.Find(".ptitle__text").TextContent);
        Assert.DoesNotContain("Last:", cut.Find(".ptitle__text").TextContent);
    }
}
