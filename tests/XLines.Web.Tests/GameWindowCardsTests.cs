using XLines.Core.Analytics;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Panels;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Game window is a flow of cards (user mock-up, Nov 22): the matchup one card, its teams
/// divided and its date a footnote at its foot with Gamecast and Place wager inside it; the score
/// by period, head to head and your bets each a card of their own; the tabs the row beneath, their
/// cards flowing again. Nothing is framed twice, and no card is stretched across a wide window.
/// </summary>
public class GameWindowCardsTests : DeskTestContext
{
    private readonly FakeGameWindowData _data = new();

    public GameWindowCardsTests() => Services.AddSingleton<IGameWindowData>(_data);

    private IRenderedComponent<GamePanel> Render()
    {
        var cut = RenderComponent<GamePanel>(p => p.Add(g => g.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ptitle")));
        return cut;
    }

    private static Game Final()
    {
        var game = BoardRows.Game();
        game.Status = GameStatus.Final;
        game.AwayScore = 46;
        game.HomeScore = 40;
        game.Linescore = """{"home":[7,10,13,10],"away":[14,3,22,7]}""";
        return game;
    }

    private static JournalEntry Bet(EntryResult result, decimal stake)
        => new() { Result = result, Stake = stake, Outcome = BoardRows.Home, Market = Markets.Moneyline, Book = "draftkings" };

    [Fact]
    public void The_window_is_a_card_flow()
    {
        var cut = Render();

        var flow = cut.Find(".wflow.gamewin");

        // The matchup leads the flow, and the lines are a card of it beside head to head, not a
        // card behind a tab: with nothing wide to show, nothing spans the row and there are no tabs.
        var cards = flow.Children.ToList();
        Assert.Contains("gamecard", cards[0].ClassName);
        Assert.Contains(cards, c => c.ClassList.Contains("gamelines"));
        Assert.DoesNotContain(cards, c => c.ClassList.Contains("wsection--span"));
        Assert.Empty(cut.FindAll("[role=tab]"));
    }

    [Fact]
    public void The_matchup_is_one_card_with_the_date_and_the_keys_at_its_foot()
    {
        var live = BoardRows.Game();
        live.Status = GameStatus.Live;
        _data.Row = BoardRows.Row() with { Game = live };

        var cut = Render();

        var group = cut.Find(".gamecard > .wgroup");
        var title = group.Children.FirstOrDefault(c => c.ClassList.Contains("ptitle"));
        Assert.NotNull(title);

        // Both clubs in the card's heading, in the scoreboard whose second row carries the divider.
        Assert.Equal(2, title!.QuerySelectorAll(".ptitle__name .scoreboard__row").Length);

        // The keys are inside the card, on the foot row beside the date line, not loose in the window.
        var foot = title.QuerySelector(".ptitle__meta");
        Assert.NotNull(foot);
        Assert.NotNull(foot!.QuerySelector(".ptitle__text"));
        Assert.Contains(foot.QuerySelectorAll(".ptitle__keys button"), b => b.TextContent.Trim() == "Gamecast");
    }

    [Fact]
    public void A_scheduled_game_s_foot_says_when_and_which_season()
    {
        var cut = Render();

        var foot = cut.Find(".gamecard .ptitle__meta .ptitle__text").TextContent;
        Assert.Contains("season", foot);
    }

    [Fact]
    public void The_score_by_period_is_a_card_of_its_own_with_no_second_frame()
    {
        _data.Row = BoardRows.Row() with { Game = Final() };

        var cut = Render();

        var card = cut.Find(".gamewin > .gamescore > .wgroup");
        var score = card.Children.FirstOrDefault(c => c.ClassList.Contains("linescore"));
        Assert.NotNull(score);
        Assert.Contains("linescore--inset", score!.ClassName);
        Assert.Equal(2, score.QuerySelectorAll("tbody tr").Length);
    }

    [Fact]
    public void A_game_with_no_line_score_draws_no_empty_card()
    {
        var cut = Render();

        Assert.Empty(cut.FindAll(".gamescore"));
    }

    [Fact]
    public void Head_to_head_is_a_card_whose_first_row_is_its_disclosure()
    {
        var cut = Render();

        var toggle = cut.Find(".gamewin > section.gameh2h > .wgroup > .wgroup__row.gameh2h__head > button.gameh2h__toggle");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Your_bets_are_a_card_named_in_its_bar_with_a_row_per_bet()
    {
        _data.Detail = GameWindowDetail.Empty with { Bets = [Bet(EntryResult.Pending, 110), Bet(EntryResult.Win, 50)] };

        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gamebets")));
        var card = cut.Find(".gamewin > section.gamebets");
        var heading = card.QuerySelector(".wgroup > .wgroup__bar > h3.wgroup__title");
        Assert.NotNull(heading);
        Assert.Equal("Your bets on this game", heading!.TextContent.Trim());
        Assert.Equal(heading.Id, card.GetAttribute("aria-labelledby"));
        Assert.Contains("Journal", card.QuerySelector(".wgroup__bar > .wgroup__keys")!.TextContent);
        Assert.Equal(2, card.QuerySelectorAll(".wgroup > .wgroup__row.gamebets__row").Length);
    }

    [Fact]
    public void Three_bets_fold_into_one_row_of_the_card()
    {
        _data.Detail = GameWindowDetail.Empty with
        {
            Bets = [Bet(EntryResult.Pending, 110), Bet(EntryResult.Pending, 125), Bet(EntryResult.Win, 50)]
        };

        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gamebets")));
        Assert.Single(cut.FindAll(".gamebets .wgroup > .wgroup__row.gamebets__fold"));
    }

    [Fact]
    public void The_lines_are_a_card_of_the_overview_named_in_its_bar()
    {
        var cut = Render();

        var lines = cut.Find(".gamewin > section.gamelines.gamewin__pair");
        var group = lines.Children.FirstOrDefault(c => c.ClassList.Contains("wgroup"));
        Assert.NotNull(group);
        var heading = group!.QuerySelector(".wgroup__bar > h3.wgroup__title");
        Assert.Equal("Lines", heading!.TextContent.Trim());
        Assert.Equal(heading.Id, lines.GetAttribute("aria-labelledby"));
        Assert.NotNull(lines.Children.FirstOrDefault(c => c.ClassList.Contains("wgroup__foot")));
    }

    [Fact]
    public void A_live_game_s_lines_card_is_named_top_bets()
    {
        var live = BoardRows.Game();
        live.Status = GameStatus.Live;
        _data.Row = BoardRows.Row() with { Game = live };

        var cut = Render();

        Assert.Equal("Top bets", cut.Find(".gamelines .wgroup__title").TextContent.Trim());
    }

    /// <summary>A side's number and its price are one unbreakable reading, apart from the side's name.</summary>
    [Fact]
    public void A_side_s_number_and_price_stay_together()
    {
        var cut = Render();

        var nums = cut.FindAll(".gamelines__price > .gamelines__num");
        Assert.NotEmpty(nums);
        Assert.All(nums, n => Assert.Null(n.QuerySelector(".gamelines__side")));

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "wwwroot", "css", "xlines.css"));
        Assert.Matches(@"\.gamelines__num \{[^}]*white-space: nowrap", css);
        Assert.Matches(@"@container wflow \(min-width: 856px\) \{\s*\.gamewin > \.gamewin__pair \{ grid-column: span 2; \}", css);
    }

    [Fact]
    public void Team_data_is_a_card_of_the_overview_with_a_row_per_side()
    {
        var recent = new[] { new TeamRecentGame(90, DateTimeOffset.UtcNow.AddDays(-7), "Denver Broncos", true, 24, 17, GameStatus.Final) };
        _data.Detail = GameWindowDetail.Empty with
        {
            HomeTeam = new TeamForm(new Team { Name = BoardRows.Home, Abbrev = "KC" }, recent, []),
            AwayTeam = new TeamForm(new Team { Name = BoardRows.Away, Abbrev = "BUF" }, recent, [])
        };

        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gameteams")));
        var card = cut.Find(".gamewin > section.gameteams");
        var heading = card.QuerySelector(".wgroup > .wgroup__bar > h3.wgroup__title");
        Assert.Equal("Team data", heading!.TextContent.Trim());
        Assert.Equal(heading.Id, card.GetAttribute("aria-labelledby"));
        var rows = card.QuerySelectorAll(".wgroup > .wgroup__row.gameteams__row");
        Assert.Equal(2, rows.Length);
        Assert.Contains(BoardRows.Away, rows[0].TextContent);
        Assert.Contains(BoardRows.Home, rows[1].TextContent);
    }

    [Fact]
    public void The_season_switch_sits_in_the_team_data_card_s_bar()
    {
        var recent = new[] { new TeamRecentGame(90, DateTimeOffset.UtcNow.AddDays(-7), "Denver Broncos", true, 24, 17, GameStatus.Final) };
        _data.Detail = GameWindowDetail.Empty with
        {
            Seasons = [2025, 2026],
            SeasonYear = 2026,
            HomeTeam = new TeamForm(new Team { Name = BoardRows.Home, Abbrev = "KC" }, recent, []),
            AwayTeam = new TeamForm(new Team { Name = BoardRows.Away, Abbrev = "BUF" }, recent, [])
        };

        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gameteams")));
        Assert.NotNull(cut.Find(".gameteams .wgroup__bar > .wgroup__keys").QuerySelector("[role=radiogroup]"));
    }

    [Fact]
    public void The_players_are_a_centred_block_with_a_column_per_side()
    {
        _data.Detail = GameWindowDetail.Empty with
        {
            Matchup = new MatchupForm(BoardRows.Game(), [Player(2, "Patrick Mahomes", true)], [Player(1, "Josh Allen", false)], 2)
        };

        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gameroster")));
        var block = cut.Find(".gamewin > .wsection--span.gameroster > section.psection.gameroster__block");
        Assert.Equal("Players", block.QuerySelector(".psection__head .panel-h")!.TextContent.Trim());

        var sides = block.QuerySelectorAll(".gameroster__cols > .gameroster__side");
        Assert.Equal(2, sides.Length);
        Assert.Contains("Josh Allen", sides[0].TextContent);
        Assert.Contains("Patrick Mahomes", sides[1].TextContent);
        Assert.NotEmpty(sides[0].QuerySelectorAll(":scope > .psection"));

        // No tabs: a game has one wide reading, and the overview holds the rest.
        Assert.Empty(cut.FindAll("[role=tab]"));

        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "wwwroot", "css", "xlines.css"));
        Assert.Matches(@"\.gameroster__block \{[^}]*max-width: 1100px;[^}]*margin-inline: auto;", css);
        Assert.Matches(@"@container wflow \(min-width: 1320px\) \{[^}]*\}\s*\.gameroster__cols \{ grid-template-columns: repeat\(2, minmax\(0, 1fr\)\); \}", css);
    }

    /// <summary>View more is the window's to keep: a market closed and opened again shows every book still.</summary>
    [Fact]
    public void An_opened_market_keeps_view_more_across_closing_and_reopening()
    {
        Services.AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<XLines.Data.XLinesDbContext>>(new JournalParlayTests.NoDatabase());
        string[] ten = ["betmgm", "bovada", "caesars", "draftkings", "espnbet", "fanduel", "fanatics", "hardrock", "pinnacle", "wynnbet"];
        BestOffer Side(string outcome, int price)
            => new(outcome, ten[0], price, null, DateTimeOffset.UtcNow, null, false, true,
                ten.Select(b => new BookPrice(b, price, null, OddsMath.ImpliedProbability(price))).ToList());
        var row = BoardRows.Row(closing: true);
        _data.Row = row with { Moneyline = new MarketPair(Markets.Moneyline, Side(BoardRows.Home, -120), Side(BoardRows.Away, 110)) };

        var cut = Render();
        int Books() => cut.FindAll(".gamelines__detail .mud-table-body .mud-table-row").Count;
        void Toggle() => cut.FindAll("button.gamelines__toggle").First(b => b.TextContent.Contains("Moneyline")).Click();

        Toggle();
        cut.WaitForAssertion(() => Assert.Equal(5, Books()));
        cut.Find(".gamelines__detail .bookmore button").Click();
        cut.WaitForAssertion(() => Assert.Equal(10, Books()));

        Toggle();
        Assert.Equal(0, Books());
        Toggle();
        cut.WaitForAssertion(() => Assert.Equal(10, Books()));
    }
    private static PlayerForm Player(int id, string name, bool home)
        => new(id, name, "QB", home, 3, DateTimeOffset.UtcNow.AddDays(-7), new Dictionary<string, double>(), new Dictionary<string, int>(), new Dictionary<string, bool>());
}