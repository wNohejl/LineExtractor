using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Place wager window: the game as its header, then four steps in one order, each saying
/// whether it is done, the one to do now, or waiting — and what it is waiting for. Only the step
/// to do is open; a finished one folds to what was chosen, with Change to reopen it, so the
/// window is as tall as the step being worked and an operator who has never logged a bet can
/// read what happens next without being told.
/// </summary>
public class WagerStepperTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render(string? market = null, string? outcome = null, bool withGame = true)
        => RenderComponent<BoardWager>(p =>
        {
            if (withGame) p.Add(x => x.GameId, BoardRows.GameId);
            if (market is not null) p.Add(x => x.Market, market);
            if (outcome is not null) p.Add(x => x.Outcome, outcome);
        });

    private static IReadOnlyList<string> States(IRenderedFragment cut)
        => cut.FindAll(".wstep").Select(s =>
            s.ClassList.Contains("wstep--done") ? "done"
            : s.ClassList.Contains("wstep--current") ? "current"
            : "todo").ToList();

    private static IReadOnlyList<bool> Open(IRenderedFragment cut)
        => cut.FindAll(".wstep").Select(s => s.ClassList.Contains("wstep--open")).ToList();

    private static string Summary(IRenderedFragment cut, string title)
        => Step(cut, title).QuerySelector(".wstep__summary")!.TextContent.Trim();

    // ---- The header -----------------------------------------------------------------------

    [Fact]
    public void From_the_board_the_game_is_the_windows_title_with_its_league_and_kickoff()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        var head = cut.Find(".whead .ptitle");
        Assert.Equal("Buffalo Bills at Kansas City Chiefs", head.QuerySelector(".ptitle__name")!.TextContent.Trim());
        Assert.Contains("NFL", head.QuerySelector(".tag")!.TextContent);
        Assert.NotNull(head.QuerySelector(".ptitle__meta"));
        Assert.DoesNotContain("Game", cut.FindAll(".wstep__title").Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public void From_the_journal_the_header_is_the_search_and_says_how_to_use_it()
    {
        var cut = Render(withGame: false);

        Assert.NotNull(cut.Find(".whead input[aria-label='Find a game']"));
        Assert.Contains("Find the game the bet was on", cut.Find(".whead__hint").TextContent);
    }

    // ---- The steps ------------------------------------------------------------------------

    [Fact]
    public void The_window_is_four_numbered_steps_in_order()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Equal(
            ["Your bet", "Price and book", "Stake", "Review and log"],
            cut.FindAll(".wstep__title").Select(t => t.TextContent.Trim()));
        Assert.Equal("ol", cut.Find(".wsteps").TagName.ToLowerInvariant());
    }

    [Fact]
    public void With_no_game_yet_every_step_waits_on_one_line()
    {
        var cut = Render(withGame: false);

        Assert.Equal(["todo", "todo", "todo", "todo"], States(cut));
        Assert.Equal([false, false, false, false], Open(cut));
        Assert.Contains("Pick a game first.", Step(cut, "Your bet").QuerySelector(".wstep__wait")!.TextContent);
        Assert.Contains("Your bet and its price come first.", Step(cut, "Review and log").QuerySelector(".wstep__wait")!.TextContent);
    }

    [Fact]
    public void With_a_side_and_its_price_the_stake_is_the_step_to_do_and_the_rest_are_folded()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Equal(["done", "done", "current", "todo"], States(cut));
        // The review is open as soon as there is a price: its checks are worth reading before
        // a stake is typed, and the Log key says what it still needs.
        Assert.Equal([false, false, true, true], Open(cut));
        Assert.Equal("step", Step(cut, "Stake").GetAttribute("aria-current"));
        Assert.Contains("Needs a stake above zero.", Step(cut, "Review and log").TextContent);
    }

    [Fact]
    public void A_bet_on_no_game_is_typed_from_end_to_end_so_nothing_folds()
    {
        var cut = Render(withGame: false);
        cut.Find("input[aria-label='Find a game']").Input("afc north");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt--none")));
        cut.Find(".game-search__opt--none").Click();
        cut.WaitForElement("input[aria-label='The bet']");

        cut.Find("input[aria-label='The bet']").Input("Steelers to win the AFC North");

        Assert.Equal(["done", "current", "todo", "todo"], States(cut));
        Assert.NotNull(cut.Find("input[aria-label='The bet']"));
        Assert.NotNull(cut.Find("input[placeholder='-110']"));
    }

    [Fact]
    public void A_folded_step_says_what_was_chosen()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Assert.Equal("Spread · Bills +2.5", Summary(cut, "Your bet"));
        Assert.Equal("-105 +2.5 at fanduel", Summary(cut, "Price and book"));
        Assert.Equal("-105", cut.Find(".wager-offer__price").TextContent.Trim());
    }

    [Fact]
    public void Change_opens_a_folded_step_and_a_new_choice_folds_it_again()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Assert.Empty(cut.FindAll(".selection-chip"));

        Change(cut, "Your bet");

        Assert.Equal([true, false, true, true], Open(cut));
        Assert.Equal("ML · Chiefs", cut.Find(".selection-chip--on").GetAttribute("aria-label"));

        cut.FindAll(".selection-chip").First(b => b.GetAttribute("aria-label") == "ML · Bills").Click();

        Assert.Equal([false, false, true, true], Open(cut));
        Assert.Equal("ML · Bills", Summary(cut, "Your bet"));
    }

    [Fact]
    public void With_a_stake_the_review_opens_beside_it_and_says_what_will_be_logged()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.Equal(["done", "done", "done", "current"], States(cut));
        // The stake stays open: it is typed, and a step that folded on the first digit would
        // take the field away mid-number.
        Assert.Equal([false, false, true, true], Open(cut));
        Assert.Contains("ML · Chiefs at -128 with draftkings", cut.Find(".wreview").TextContent);
    }

    [Fact]
    public void A_done_step_shows_a_check_instead_of_its_number()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Empty(Step(cut, "Your bet").QuerySelector(".wstep__dot")!.TextContent.Trim());
        Assert.NotNull(Step(cut, "Your bet").QuerySelector(".wstep__dot svg"));
        Assert.Equal("3", Step(cut, "Stake").QuerySelector(".wstep__dot")!.TextContent.Trim());
    }

    // ---- Your bet -------------------------------------------------------------------------

    [Fact]
    public void The_bet_is_a_board_with_a_column_per_team_and_a_row_per_market()
    {
        Data.Games = [BoardRows.Game()];
        var cut = Render(withGame: false);
        cut.Find("input[aria-label='Find a game']").Input("chiefs");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt")));
        cut.FindAll(".game-search__opt")[0].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".selection-chip")));

        Assert.Equal(["Bills", "Chiefs"], cut.FindAll(".selection-board__team").Select(t => t.TextContent.Trim()));
        var labels = cut.FindAll(".selection-group__label");
        Assert.Equal(["Moneyline", "Spread", "Total"], labels.Select(l => l.TextContent.Trim()));
        Assert.Contains("wins the game", labels[0].GetAttribute("title"));

        // Cells read in column order — away, then home; Over, then Under — and carry only what
        // differs: the line, and the price.
        Assert.Equal(
            ["ML · Bills", "ML · Chiefs", "Spread · Bills +2.5", "Spread · Chiefs -2.5", "Total · Over o47.5", "Total · Under u47.5"],
            cut.FindAll(".selection-chip").Select(c => c.GetAttribute("aria-label")));
        var chiefs = cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Chiefs");
        Assert.Null(chiefs.QuerySelector(".selection-chip__side"));
        Assert.Equal("-128", chiefs.QuerySelector(".selection-chip__price")!.TextContent.Trim());
        Assert.Equal("Kansas City Chiefs win the game.", chiefs.GetAttribute("title"));
        Assert.Equal("-2.5", cut.FindAll(".selection-chip")[3].QuerySelector(".selection-chip__side")!.TextContent.Trim());
    }

    [Fact]
    public void Picking_a_side_prices_it_at_the_best_book_and_opens_the_stake()
    {
        Data.Games = [BoardRows.Game()];
        var cut = Render(withGame: false);
        cut.Find("input[aria-label='Find a game']").Input("chiefs");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".game-search__opt")));
        cut.FindAll(".game-search__opt")[0].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".selection-chip")));
        Assert.Equal(["current", "todo", "todo", "todo"], States(cut));

        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Chiefs").Click();

        Assert.Equal(["done", "done", "current", "todo"], States(cut));
        Assert.Equal("-128 at draftkings", Summary(cut, "Price and book"));
    }

    // ---- Price and book -------------------------------------------------------------------

    [Fact]
    public void The_edit_key_sits_on_the_price()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Change(cut, "Price and book");

        var key = cut.Find(".wager-offer__top .wager-manual-key");
        Assert.Equal("Enter my own price", key.GetAttribute("title"));
        Assert.Contains("wager-offer__price", key.PreviousElementSibling!.ClassList);
    }

    [Fact]
    public void The_book_is_a_menu_on_the_price_that_lists_every_book_with_its_number()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Change(cut, "Price and book");

        var trigger = cut.Find(".wager-offer .book-picker__trigger");
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Contains("draftkings", trigger.TextContent);
        Assert.Empty(cut.FindAll(".book-row"));

        trigger.Click();

        Assert.Equal("true", cut.Find(".book-picker__trigger").GetAttribute("aria-expanded"));
        var rows = cut.FindAll(".book-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("-135", rows[1].TextContent);

        rows[1].Click();

        // Chosen: the step folds on the new number.
        Assert.Empty(cut.FindAll(".book-row"));
        Assert.Equal("-135 at fanduel", Summary(cut, "Price and book"));
    }

    [Fact]
    public void Escape_closes_the_book_menu()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
        Change(cut, "Price and book");
        cut.Find(".book-picker__trigger").Click();

        cut.Find(".book-picker").KeyDown("Escape");

        Assert.Empty(cut.FindAll(".book-row"));
    }

    // ---- The game search ------------------------------------------------------------------

    private static Game Saints(int id, DateTimeOffset startsAt, GameStatus status = GameStatus.Scheduled) => new()
    {
        Id = id,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        HomeTeam = new Team { Name = "New Orleans Saints", Abbrev = "NO" },
        AwayTeam = new Team { Name = "Pittsburgh Steelers", Abbrev = "PIT" },
        StartsAt = startsAt,
        Status = status
    };

    [Fact]
    public void The_search_list_says_which_games_are_coming_and_which_are_played()
    {
        Data.Games = [Saints(1, DateTimeOffset.UtcNow.AddDays(1)), Saints(2, DateTimeOffset.UtcNow.AddDays(-6), GameStatus.Final)];
        var cut = Render(withGame: false);

        cut.Find("input[aria-label='Find a game']").Input("steelers");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Upcoming", "Played"],
            cut.FindAll(".game-search__group").Select(g => g.TextContent.Trim())));
    }
}
