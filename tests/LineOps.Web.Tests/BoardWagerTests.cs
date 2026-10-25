using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Place wager window: opened on the side a door names, the price that side's book shows, a
/// Log key that says what it is waiting for, and a loading state that is not an empty one.
/// </summary>
public class BoardWagerTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render(string? market = null, string? outcome = null, string? book = null)
        => RenderComponent<BoardWager>(p =>
        {
            p.Add(x => x.GameId, BoardRows.GameId);
            if (market is not null) p.Add(x => x.Market, market);
            if (outcome is not null) p.Add(x => x.Outcome, outcome);
            if (book is not null) p.Add(x => x.Book, book);
        });

    private static IElement Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private static IElement Log(IRenderedFragment cut) => cut.Find("button.desk-btn--filled");

    [Fact]
    public void With_only_a_game_it_lands_on_the_side_with_the_most_value()
    {
        var cut = Render();

        Assert.Equal("-128", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.StartsWith("ML · Chiefs · -128 at draftkings", System.Text.RegularExpressions.Regex.Replace(Step(cut, "Your bet").QuerySelector(".wstep__summary")!.TextContent, @"\s+", " ").Trim());
    }

    [Fact]
    public void A_door_that_names_a_side_lands_on_it()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Assert.Equal("-105", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.StartsWith("Spread · Bills +2.5 · -105 +2.5 at fanduel", System.Text.RegularExpressions.Regex.Replace(Step(cut, "Your bet").QuerySelector(".wstep__summary")!.TextContent, @"\s+", " ").Trim());
    }

    [Fact]
    public void A_door_that_names_a_book_shows_that_books_price()
    {
        var cut = Render(Markets.Moneyline, BoardRows.Home, "fanduel");

        Assert.Equal("-135", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.Equal("fanduel", cut.Find(".wager-offer__book").TextContent.Trim());
    }

    [Fact]
    public void Log_is_disabled_at_zero_stake_and_says_why()
    {
        var cut = Render();

        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Equal("Needs a stake above zero.", cut.Find(".wager-why").TextContent.Trim());
    }

    [Fact]
    public void While_the_row_loads_the_body_is_a_skeleton_not_an_empty_state()
    {
        Data.RowGate = new TaskCompletionSource();
        var cut = Render();

        Assert.NotEmpty(cut.FindAll(".skel__line"));
        Assert.DoesNotContain("Pick a side", cut.Markup);

        Data.RowGate.SetResult();
        cut.WaitForAssertion(() => Assert.Equal("-128", cut.Find(".wager-offer__price").TextContent.Trim()));
    }

    [Fact]
    public void Logging_writes_one_straight_bet_through_the_journal()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Equal(BoardRows.GameId, bet.GameId);
        Assert.Equal(Markets.Moneyline, bet.Market);
        Assert.Equal(BoardRows.Home, bet.Outcome);
        Assert.Equal("draftkings", bet.Book);
        Assert.Equal(-128, bet.PriceTaken);
        Assert.Equal(50m, bet.Stake);

        // The stake clears so the lit key cannot log the same wager twice; the side stays.
        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Contains($"Logged {50m:C0} on ML · Chiefs at -128 (draftkings).", cut.Find(".wager-logged").TextContent);
    }

    // ---- Before you log -------------------------------------------------------------------

    [Fact]
    public void The_strip_reads_value_age_and_the_journal()
    {
        var cut = Render();

        var rows = cut.FindAll(".check").Select(r => r.TextContent).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("Value: +2.7% against fair -136 (Pinnacle, no-vig).", rows[0]);
        Assert.Contains("Price captured 12 min ago.", rows[1]);
        Assert.Contains("Nothing logged on this side yet.", rows[2]);
    }

    [Fact]
    public void A_prior_bet_on_the_side_is_named_and_Log_stays_available()
    {
        Data.Latest = new JournalEntry
        {
            GameId = BoardRows.GameId,
            Market = Markets.Moneyline,
            Outcome = BoardRows.Home,
            Book = "draftkings",
            PriceTaken = -130,
            Stake = 50m,
            PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var cut = Render();
        Key(cut, 25m.ToString("C0")).Click();

        var warning = cut.Find(".check--warning").TextContent;
        Assert.Contains($"Already in the journal: {50m:C0} on ML · Chiefs at -130 (draftkings)", warning);
        Assert.Contains("Logging again records a second bet.", warning);
        Assert.False(Log(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void A_closing_offer_disables_Log_and_keeps_Add_to_slip()
    {
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();

        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Equal("This is the closing line. Enter my own price records the one you got.", cut.Find(".wager-why").TextContent.Trim());
        Assert.Contains("Closing line: the game started 42 min ago", cut.Find(".check--negative").TextContent);

        Key(cut, "Add to slip").Click();

        var item = Assert.Single(Slip.Items);
        Assert.True(item.IsClosing);
    }

    [Fact]
    public void Add_to_slip_counts_on_the_badge_and_clears_the_stake()
    {
        var cut = Render();
        Assert.Equal("0", cut.Find(".slip-badge").TextContent.Trim());

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();

        Assert.Equal("1", cut.Find(".slip-badge").TextContent.Trim());
        Assert.Equal(50m, Slip.Items.Single().Stake);
        Assert.Contains($"Added {50m:C0} on ML · Chiefs at -128 to the slip.", cut.Find(".wager-logged").TextContent);
        Assert.True(Log(cut).HasAttribute("disabled"));   // the stake is cleared
    }

    [Fact]
    public void A_wager_banked_over_a_journal_bet_carries_it_onto_the_slip()
    {
        Data.Latest = new JournalEntry
        {
            GameId = BoardRows.GameId,
            Market = Markets.Moneyline,
            Outcome = BoardRows.Home,
            Book = "draftkings",
            PriceTaken = -130,
            Stake = 50m,
            PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var cut = Render();

        Key(cut, 25m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();

        Assert.StartsWith($"{50m:C0} at -130 (draftkings)", Slip.Items.Single().InJournal);
    }

    [Fact]
    public void A_note_typed_and_banked_straight_away_goes_with_the_wager()
    {
        var cut = Render();

        Key(cut, 25m.ToString("C0")).Click();
        cut.Find("input[aria-label='Note']").Input("fade the public");
        Key(cut, "Add to slip").Click();

        Assert.Equal("fade the public", Slip.Items.Single().Note);
    }

    [Fact]
    public void The_capture_age_reads_in_the_same_words_as_the_check_beneath_it()
    {
        Data.Row = BoardRows.Row(age: TimeSpan.FromMinutes(109.5));
        var cut = Render();
        Change(cut, "Your bet");

        Assert.Contains("captured 1h 49m ago", cut.Find(".wager-offer__age").TextContent);
        Assert.Contains("Price captured 1h 49m ago", cut.Markup);
    }

    [Fact]
    public void The_badge_follows_the_slip_when_another_window_changes_it()
    {
        var cut = Render();

        cut.InvokeAsync(() => Slip.Add(new LineOps.Web.Services.SlipItem(Guid.Empty, 8, "x", DateTimeOffset.UtcNow, "nfl",
            Markets.Moneyline, "x", null, "draftkings", -110, DateTimeOffset.UtcNow, false, 10m, null, null, null)));

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".slip-badge").TextContent.Trim()));
    }

    [Fact]
    public void An_odds_change_reloads_and_says_the_price_moved()
    {
        var cut = Render();
        Data.Row = BoardRows.Row(homeMl: -122);

        Services.GetRequiredService<LineOps.Desk.Windowing.DeskSignals>()
            .Publish(new HashSet<string> { LineOps.Data.Changes.DataTopics.Odds });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("-122", cut.Find(".wager-offer__price").TextContent.Trim());
            Assert.Contains("Price moved since you opened this: -128 → -122 (draftkings).", cut.Find(".wager-moved").TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_stale_price_offers_the_leagues_pull_at_its_cost_and_runs_it()
    {
        Data.Row = BoardRows.Row(age: TimeSpan.FromMinutes(45));
        Data.LinesJob = new LineOps.Ingestion.Services.IngestionJob(
            "odds:lines:nfl", "NFL lines", "Lines for NFL", 1, true, null, EstimatedCredits: 2);

        var cut = Render();
        var reads = Data.RowReads;

        Key(cut, "Pull NFL lines · 2 credits").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "odds:lines:nfl" }, Data.JobsRun);
            Assert.True(Data.RowReads > reads);
        });
    }
}
