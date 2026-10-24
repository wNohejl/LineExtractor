using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Place wager window as a numbered stepper: five steps in one order, each saying whether it
/// is done, the one to do now, or waiting — and what it is waiting for — so an operator who has
/// never logged a bet can read what happens next without being told.
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

    private static IElement Step(IRenderedFragment cut, int number) => cut.FindAll(".wstep")[number - 1];

    // ---- The steps ------------------------------------------------------------------------

    [Fact]
    public void The_window_is_five_numbered_steps_in_order()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Equal(
            ["Game", "Your bet", "Price and book", "Stake", "Review and log"],
            cut.FindAll(".wstep__title").Select(t => t.TextContent.Trim()));
        Assert.Equal("ol", cut.Find(".wsteps").TagName.ToLowerInvariant());
    }

    [Fact]
    public void Opened_from_the_journal_only_the_game_step_is_open()
    {
        var cut = Render(withGame: false);

        Assert.Equal(["current", "todo", "todo", "todo", "todo"], States(cut));
        Assert.Equal("step", Step(cut, 1).GetAttribute("aria-current"));
    }

    [Fact]
    public void With_a_side_and_its_price_the_stake_is_the_step_to_do()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Equal(["done", "done", "done", "current", "todo"], States(cut));
    }

    [Fact]
    public void With_a_stake_the_review_is_the_step_to_do_and_says_what_will_be_logged()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.Equal(["done", "done", "done", "done", "current"], States(cut));
        Assert.Contains("ML · Chiefs at -128 with draftkings", cut.Find(".wreview").TextContent);
    }

    [Fact]
    public void A_waiting_step_says_what_it_needs()
    {
        Data.Games = [BoardRows.Game()];
        var cut = Render(withGame: false);

        Assert.Contains("Pick a game first.", Step(cut, 2).TextContent);
        Assert.Contains("Pick your bet first.", Step(cut, 3).TextContent);
    }

    [Fact]
    public void A_done_step_shows_a_check_instead_of_its_number()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Empty(Step(cut, 1).QuerySelector(".wstep__dot")!.TextContent.Trim());
        Assert.NotNull(Step(cut, 1).QuerySelector(".wstep__dot svg"));
        Assert.Equal("4", Step(cut, 4).QuerySelector(".wstep__dot")!.TextContent.Trim());
    }

    [Fact]
    public void The_game_is_shown_when_the_board_opened_the_window()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        Assert.Contains("Buffalo Bills at Kansas City Chiefs", Step(cut, 1).TextContent);
    }

    // ---- Your bet -------------------------------------------------------------------------

    [Fact]
    public void Sides_are_grouped_by_market_under_a_label_that_explains_it()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        var labels = cut.FindAll(".selection-group__label");
        Assert.Equal(["Moneyline", "Spread", "Total"], labels.Select(l => l.TextContent.Trim()));
        Assert.Contains("wins the game", labels[0].GetAttribute("title"));
    }

    [Fact]
    public void Each_side_shows_its_best_price_and_says_in_words_what_it_wins_on()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        var chiefs = cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Chiefs");
        Assert.Equal("Chiefs", chiefs.QuerySelector(".selection-chip__side")!.TextContent.Trim());
        Assert.Equal("-128", chiefs.QuerySelector(".selection-chip__price")!.TextContent.Trim());
        Assert.Equal("true", chiefs.GetAttribute("aria-checked"));
        Assert.Equal("Kansas City Chiefs win the game.", chiefs.GetAttribute("title"));
    }

    // ---- Price and book -------------------------------------------------------------------

    [Fact]
    public void The_edit_key_sits_on_the_price()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

        var key = cut.Find(".wager-offer__top .wager-manual-key");
        Assert.Equal("Enter my own price", key.GetAttribute("title"));
        Assert.Contains("wager-offer__price", key.PreviousElementSibling!.ClassList);
    }

    [Fact]
    public void The_book_is_a_menu_on_the_price_that_lists_every_book_with_its_number()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);

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

        Assert.Empty(cut.FindAll(".book-row"));
        Assert.Equal("-135", cut.Find(".wager-offer__price").TextContent.Trim());
    }

    [Fact]
    public void Escape_closes_the_book_menu()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home);
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
