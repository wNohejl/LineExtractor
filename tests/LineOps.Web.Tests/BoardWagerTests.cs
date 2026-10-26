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

    private static string SlipTab(IRenderedFragment cut) => cut.FindAll("[role=tab]")[1].TextContent.Trim();

    [Fact]
    public void With_only_a_game_it_lands_on_the_side_with_the_most_value()
    {
        var cut = Render();

        Assert.Equal("-128", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.StartsWith("ML · Chiefs · -128 at draftkings", System.Text.RegularExpressions.Regex.Replace(Step(cut, "Bet").QuerySelector(".wstep__summary")!.TextContent, @"\s+", " ").Trim());
    }

    [Fact]
    public void A_door_that_names_a_side_lands_on_it()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Assert.Equal("-105", cut.Find(".wager-offer__price").TextContent.Trim());
        Assert.StartsWith("Spread · Bills +2.5 · -105 +2.5 at fanduel", System.Text.RegularExpressions.Regex.Replace(Step(cut, "Bet").QuerySelector(".wstep__summary")!.TextContent, @"\s+", " ").Trim());
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

        // The steps give way to the receipt: what was logged, and what to do next.
        var receipt = cut.Find(".wreceipt");
        Assert.Equal("Logged in the journal", receipt.QuerySelector(".wreceipt__title")!.TextContent.Trim());
        Assert.Equal($"{50m:C0} on ML · Chiefs at -128 · draftkings", Squash(receipt.QuerySelector(".wreceipt__bet")!.TextContent));
        Assert.Empty(cut.FindAll(".wsteps"));

        // Same bet again is the old behaviour: the side stays, the stake is asked for again, so
        // the lit key cannot log the same wager twice.
        Key(cut, "Same bet again").Click();
        Assert.Empty(cut.FindAll(".wreceipt"));
        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Contains("Chiefs", cut.FindAll(".wstep__summary").First().TextContent);
    }

    [Fact]
    public void New_bet_clears_what_the_logged_bet_chose_and_opens_the_first_step()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        // New bet is the receipt's one Filled key, and the last.
        var keys = cut.Find(".wreceipt__keys").QuerySelectorAll("button").ToList();
        Assert.Equal("New bet", keys[^1].TextContent.Trim());
        Assert.Contains("desk-btn--filled", keys[^1].ClassList);
        Assert.Single(keys, k => k.ClassList.Contains("desk-btn--filled"));

        keys[^1].Click();

        // Step 1 again, nothing chosen; the review and its Log key wait for a price.
        Assert.Empty(cut.FindAll(".wreceipt"));
        Assert.Empty(cut.FindAll(".wbet [role=radio][aria-checked=true]"));
        Assert.NotNull(cut.Find(".wbet__wait"));
        Assert.Empty(cut.FindAll("button.desk-btn--filled"));

        // A re-render handing the window the same door does not land it back on the old side.
        cut.SetParametersAndRender(p => p.Add(x => x.Market, Markets.Spread).Add(x => x.Outcome, BoardRows.Away));
        Assert.Empty(cut.FindAll(".wbet [role=radio][aria-checked=true]"));
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
    public void A_journal_that_cannot_be_read_warns_that_a_duplicate_is_not_ruled_out()
    {
        Data.LatestFailWith = new InvalidOperationException("journal table locked");

        var cut = Render();
        Key(cut, 25m.ToString("C0")).Click();

        var warning = cut.Find(".check--warning").TextContent;
        Assert.Contains("The journal could not be read, so a duplicate cannot be ruled out.", warning);
        Assert.DoesNotContain("Reading the journal for this side.", cut.Markup);
        Assert.DoesNotContain("journal table locked", cut.Markup);
        Assert.False(Log(cut).HasAttribute("disabled"));
    }

    /// <summary>
    /// Going back to the fetched price on a started game is the operator's choice, and brings the
    /// old block back with it: the close cannot be logged, says so once under the card, and can
    /// still be banked on the slip, where it asks for the price that was taken.
    /// </summary>
    [Fact]
    public void Going_back_to_a_started_games_close_restores_the_block()
    {
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Assert.False(Log(cut).HasAttribute("disabled"));

        cut.Find(".wager-manual-key").Click();

        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Contains("This is the closing line, so it cannot be logged", cut.Find(".wstatus__text").TextContent);
        Assert.Empty(cut.FindAll(".wager-why"));
        Assert.Contains("Closing line: the game started 42 min ago", cut.Find(".check--negative").TextContent);

        Key(cut, "Add to slip").Click();

        var item = Assert.Single(Slip.Items);
        Assert.True(item.IsClosing);
    }

    [Fact]
    public void Add_to_slip_counts_on_the_slip_tab_and_clears_the_stake()
    {
        var cut = Render();
        Assert.Equal("Slip · 0", SlipTab(cut));

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();

        // The count is the Slip tab's: with the stake cleared, Add to slip (and its badge) waits out of sight.
        Assert.Equal("Slip · 1", SlipTab(cut));
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
        Change(cut, "Bet");

        Assert.Contains("captured 1h 49m ago", cut.Find(".wager-offer__age").TextContent);
        Assert.Contains("Price captured 1h 49m ago", cut.Markup);
    }

    [Fact]
    public void The_slip_count_follows_the_slip_when_another_window_changes_it()
    {
        var cut = Render();

        cut.InvokeAsync(() => Slip.Add(new LineOps.Web.Services.SlipItem(Guid.Empty, 8, "x", DateTimeOffset.UtcNow, "nfl",
            Markets.Moneyline, "x", null, "draftkings", -110, DateTimeOffset.UtcNow, false, 10m, null, null, null)));

        cut.WaitForAssertion(() => Assert.Equal("Slip · 1", SlipTab(cut)));
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
