using AngleSharp.Dom;
using Bunit;
using LineOps.Desk.Primitives;
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
    /// <summary>
    /// A door that names the Chiefs moneyline, as a price pressed on the Board does. Place wager on
    /// a row names no side and opens on step 1 (<see cref="PlaceWagerOnARow"/>).
    /// </summary>
    private IRenderedComponent<BoardWager> Render(string? market = Markets.Moneyline, string? outcome = BoardRows.Home, string? book = null)
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

    /// <summary>The Slip tab's count: its badge's figure, or null where there is no badge.</summary>
    private static string? SlipCount(IRenderedFragment cut)
        => cut.FindAll("[role=tab]").Single(t => t.TextContent.Trim().StartsWith("Slip", StringComparison.Ordinal))
              .QuerySelector(".badge")?.TextContent.Trim();

    private IRenderedComponent<BoardWager> PlaceWagerOnARow()
        => RenderComponent<BoardWager>(p => p.Add(x => x.GameId, BoardRows.GameId));

    /// <summary>
    /// Place wager on a row names only the game, and the window asks: step 1 open, every side
    /// offered, none chosen. It used to land on the side with the most value, a bet the operator
    /// had not made, already folded away.
    /// </summary>
    [Fact]
    public void With_only_a_game_it_opens_on_step_one_with_no_side_chosen()
    {
        var cut = PlaceWagerOnARow();

        Assert.Contains("wstep--current", Step(cut, "Bet").ClassList);
        Assert.NotEmpty(cut.FindAll(".selection-chip"));
        Assert.Empty(cut.FindAll(".wbet [role=radio][aria-checked=true]"));
        Assert.NotNull(cut.Find(".wbet__wait"));
        Assert.Empty(cut.FindAll("button.desk-btn--filled"));
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
        Assert.Equal("Needs a stake above zero.", cut.Find(".keyrow__reason").TextContent.Trim());
    }

    /// <summary>
    /// The reason is the Filled key's description as well as text on the row (KeyRow), so a
    /// screen reader hears why Log waits; with nothing to wait for, Log points at nothing.
    /// </summary>
    [Fact]
    public void Log_names_what_it_waits_for()
    {
        var cut = Render();

        var id = Log(cut).GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal("Needs a stake above zero.", cut.Find($"#{id}").TextContent.Trim());

        Key(cut, 50m.ToString("C0")).Click();

        Assert.Null(Log(cut).GetAttribute("aria-describedby"));
        Assert.Empty(cut.FindAll(".keyrow__reason"));
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

        // What was logged is one line above the steps, which have started the next bet.
        var receipt = cut.Find(".wlogged");
        Assert.Equal("Logged", receipt.QuerySelector(".tag")!.TextContent.Trim());
        Assert.Equal($"{50m:C0} on ML · Chiefs at -128 · draftkings", Squash(receipt.QuerySelector(".wreceipt__bet")!.TextContent));
        Assert.NotEmpty(cut.FindAll(".wsteps"));

        // Same bet again puts the side back and asks for the stake again, so no lit key can log
        // the same wager twice.
        Key(cut, "Same bet again").Click();
        Assert.Empty(cut.FindAll(".wlogged"));
        Assert.True(Log(cut).HasAttribute("disabled"));
        Assert.Contains("Chiefs", cut.FindAll(".wstep__summary").First().TextContent);
    }

    /// <summary>
    /// A successful Log resets the wizard as New bet did — the game kept, the side, price, stake
    /// and note gone, step 1 open — and the receipt is a line above the steps with two plain
    /// keys. It used to stand in the steps' place until New bet was pressed.
    /// </summary>
    [Fact]
    public void Logging_resets_the_wizard_to_its_first_step_under_a_line_saying_what_was_logged()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);

        Key(cut, 50m.ToString("C0")).Click();
        cut.Find("input[aria-label='Note']").Input("buy the hook");
        Log(cut).Click();

        // The line's keys are plain: the next bet's Filled key is the steps' own.
        var keys = cut.Find(".wlogged__keys").QuerySelectorAll("button").Select(k => k.TextContent.Trim()).ToList();
        Assert.Equal(["Same bet again", "View in journal"], keys);
        Assert.Empty(cut.Find(".wlogged").QuerySelectorAll("button.desk-btn--filled"));

        // Step 1 again, nothing chosen; the review and its Log key wait for a price.
        Assert.Contains("wstep--current", Step(cut, "Bet").ClassList);
        Assert.Empty(cut.FindAll(".wbet [role=radio][aria-checked=true]"));
        Assert.NotNull(cut.Find(".wbet__wait"));
        Assert.Empty(cut.FindAll("button.desk-btn--filled"));

        // A re-render handing the window the same door does not land it back on the old side.
        cut.SetParametersAndRender(p => p.Add(x => x.Market, Markets.Spread).Add(x => x.Outcome, BoardRows.Away));
        Assert.Empty(cut.FindAll(".wbet [role=radio][aria-checked=true]"));

        // Picking a side is the next bet: the line goes, and the note did not carry over.
        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Chiefs").Click();
        Assert.Empty(cut.FindAll(".wlogged"));
        Assert.Equal("", cut.Find("input[aria-label='Note']").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Two_bets_log_one_after_the_other_without_New_bet()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Log(cut).Click();

        cut.FindAll(".selection-chip").First(c => c.GetAttribute("aria-label") == "ML · Bills").Click();
        Key(cut, 25m.ToString("C0")).Click();
        Log(cut).Click();

        Assert.Equal([(BoardRows.Home, 50m), (BoardRows.Away, 25m)], Data.Straights.Select(b => (b.Outcome, b.Stake)));
        Assert.StartsWith($"{25m:C0} on ML · Bills", Squash(cut.Find(".wlogged .wreceipt__bet").TextContent));
    }

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---- Prices to the cent ----------------------------------------------------------------

    /// <summary>Types a price of your own on the Chiefs moneyline, and a stake.</summary>
    private IRenderedComponent<BoardWager> TypedAt(string price, string stake)
    {
        var cut = Render();
        if (cut.FindAll(".wager-manual-key").Count == 0)
            Change(cut, "Bet");
        cut.Find(".wager-manual-key").Click();

        cut.Find("input[aria-label='Price']").Input(price);
        cut.Find("input[aria-label='Stake']").Input(stake);
        return cut;
    }

    /// <summary>
    /// Fanatics quoted +104.35 on $500. The field kept 104 silently and the window said "To win
    /// $520": the cents were neither typed nor paid.
    /// </summary>
    [Fact]
    public void A_price_to_the_cent_is_paid_to_the_cent()
    {
        using var dollars = new UsDollars();
        var cut = TypedAt("104.35", "500");

        Assert.Equal("To win $521.75 · $1,021.75 back with the stake", Squash(cut.Find(".wstake__win").TextContent));
        Assert.Contains("ML · Chiefs at +104.35", Squash(cut.Find(".wreview").TextContent));
        Assert.Contains("It returns $1,021.75 if it wins.", Squash(cut.Find(".wreview").TextContent));

        Log(cut).Click();

        var bet = Assert.Single(Data.Straights);
        Assert.Equal(104.35m, bet.PriceTaken);
        Assert.StartsWith("$500 on ML · Chiefs at +104.35", Squash(cut.Find(".wreceipt__bet").TextContent));
    }

    [Fact]
    public void A_whole_price_shows_whole_dollars()
    {
        using var dollars = new UsDollars();
        var cut = TypedAt("104", "500");

        Assert.Equal("To win $520 · $1,020 back with the stake", Squash(cut.Find(".wstake__win").TextContent));
    }

    /// <summary>Decimal odds, what a book prints beside the American price, read as that price and said back.</summary>
    [Fact]
    public void Decimal_odds_are_read_as_the_american_price_they_stand_for()
    {
        using var dollars = new UsDollars();
        var cut = TypedAt("2.0435", "500");

        Assert.Contains("Decimal 2.0435 = +104.35", cut.Find(".wager-typed .wbet__echo").TextContent);
        Assert.Equal("To win $521.75 · $1,021.75 back with the stake", Squash(cut.Find(".wstake__win").TextContent));

        Log(cut).Click();
        Assert.Equal(104.35m, Assert.Single(Data.Straights).PriceTaken);
    }

    /// <summary>
    /// The book's own decimal odds carry the price behind the one it prints: 2.04348 is +104.348,
    /// shown as +104.35, and pays the $1,021.74 the book pays — not the $1,021.75 of +104.35.
    /// </summary>
    [Fact]
    public void The_books_decimal_odds_pay_what_the_book_pays()
    {
        using var dollars = new UsDollars();
        var cut = TypedAt("2.04348", "500");

        Assert.Contains("Decimal 2.04348 = +104.35", cut.Find(".wager-typed .wbet__echo").TextContent);
        Assert.Equal("To win $521.74 · $1,021.74 back with the stake", Squash(cut.Find(".wstake__win").TextContent));

        Log(cut).Click();
        Assert.Equal(104.348m, Assert.Single(Data.Straights).PriceTaken);
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
        Assert.Empty(cut.FindAll(".keyrow__reason"));
        Assert.Contains("Closing line: the game started 42 min ago", cut.Find(".check--negative").TextContent);

        Key(cut, "Add to slip").Click();

        var item = Assert.Single(Slip.Items);
        Assert.True(item.IsClosing);
    }

    /// <summary>
    /// Add to slip banks the wager and leaves it on screen as it was: the stake stays, Log stays
    /// lit, and the review says the bet is on the slip until something about it changes. It used
    /// to clear the stake, so the stake step folded empty and the bet looked lost.
    /// </summary>
    [Fact]
    public void Add_to_slip_counts_on_the_slip_tab_and_keeps_the_bet()
    {
        var cut = Render();
        Assert.Null(SlipCount(cut));

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();

        Assert.Equal("1", SlipCount(cut));
        Assert.Equal(50m, Slip.Items.Single().Stake);
        // Said as an addition: a green toast with a key to the slip.
        Assert.Equal($"Added {50m:C0} on ML · Chiefs at -128 to the slip.", LastToast.Message);
        Assert.Equal(DeskState.Positive, LastToast.State);
        Assert.Equal("View slip", LastToast.ActionLabel);
        Assert.False(Log(cut).HasAttribute("disabled"));   // the stake stays
        Assert.Equal("On the slip", cut.Find(".wbanked .tag").TextContent.Trim());

        // A different stake is a different wager: the tag goes, and Add to slip banks a second card.
        Key(cut, 25m.ToString("C0")).Click();
        Assert.Empty(cut.FindAll(".wbanked"));

        Key(cut, "Add to slip").Click();

        Assert.Equal([50m, 25m], Slip.Items.Select(i => i.Stake));
        Assert.Equal("2", SlipCount(cut));
        Assert.NotNull(cut.Find(".wbanked"));
    }

    [Fact]
    public void The_on_the_slip_tag_goes_when_the_card_leaves_the_slip()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();
        cut.InvokeAsync(() => Slip.Clear());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wbanked")));
    }

    /// <summary>
    /// Log after Add to slip, with the bet unchanged, is the banked bet going to the journal: its
    /// card leaves the slip, so the slip's own Log cannot record it a second time. A card the
    /// bet no longer matches (a new stake since) stays, because it is a different wager.
    /// </summary>
    [Fact]
    public void Logging_the_bet_still_banked_takes_its_card_off_the_slip()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();
        Assert.Single(Slip.Items);

        Log(cut).Click();

        cut.WaitForAssertion(() => Assert.Empty(Slip.Items));
        Assert.Null(SlipCount(cut));
    }

    [Fact]
    public void Logging_a_changed_bet_leaves_the_banked_card_on_the_slip()
    {
        var cut = Render();

        Key(cut, 50m.ToString("C0")).Click();
        Key(cut, "Add to slip").Click();
        Key(cut, 25m.ToString("C0")).Click();

        Log(cut).Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".wlogged")));
        Assert.Equal(50m, Slip.Items.Single().Stake);
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

        cut.WaitForAssertion(() => Assert.Equal("1", SlipCount(cut)));
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
