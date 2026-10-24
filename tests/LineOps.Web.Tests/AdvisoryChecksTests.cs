using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Desk;
using LineOps.Web.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The checks the wager window runs before Log. Pure: an offer, a clock and a journal reading in,
/// rows out. Only the closing line blocks; every other row is a fact the operator may act against.
/// </summary>
public class AdvisoryChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 17, 0, 0, TimeSpan.Zero);
    private static readonly FairPrice Pinnacle = new(0.576, -136, FairValue.SharpBook, 1);

    private static WagerOffer Offer(TimeSpan? age = null, bool closing = false, double? ev = 0.027, FairPrice? fair = null, bool noFair = false)
        => new(Markets.Moneyline, "Kansas City Chiefs", null, "draftkings", -128,
            Now - (age ?? TimeSpan.FromMinutes(12)), closing, noFair ? null : fair ?? Pinnacle, ev, 4, true);

    private static readonly CheckActions Actions = new("Pull NFL lines · 2 credits", () => Task.CompletedTask, () => Task.CompletedTask);

    [Fact]
    public void A_live_offer_reads_value_age_and_the_journal_in_that_order()
    {
        var rows = Checks.Build(Offer(), Now.AddHours(3), Now, priorRead: true, prior: null, stake: 0m, Actions);

        Assert.Equal(3, rows.Count);
        Assert.StartsWith("Value:", rows[0].Text);
        Assert.StartsWith("Price captured", rows[1].Text);
        Assert.Equal("Nothing logged on this side yet.", rows[2].Text);
    }

    [Fact]
    public void Value_against_a_fair_price_is_positive_and_names_what_it_is_measured_against()
    {
        var value = Checks.Value(Offer(), 0m);

        Assert.Equal(DeskState.Positive, value.State);
        Assert.Equal("Value: +2.7% against fair -136 (Pinnacle, no-vig).", value.Text);
    }

    [Fact]
    public void No_value_is_a_warning_not_a_block()
    {
        var value = Checks.Value(Offer(ev: -0.076, fair: new FairPrice(0.42, 136, FairValue.SharpBook, 1)), 0m);

        Assert.Equal(DeskState.Warning, value.State);
        Assert.Equal("No value: \u22127.6% against fair +136 (Pinnacle, no-vig).", value.Text);
    }

    [Fact]
    public void The_stake_adds_what_the_value_comes_to()
    {
        var value = Checks.Value(Offer(), 50m);

        Assert.EndsWith($" expected on {50m:C0}.", value.Text);
        Assert.Contains($"+{1.35m:C2}", value.Text);
    }

    [Fact]
    public void No_fair_price_is_unread_rather_than_zero()
    {
        var value = Checks.Value(Offer(ev: null, noFair: true), 50m);

        Assert.Equal(DeskState.Neutral, value.State);
        Assert.Equal("No fair price on this number: fewer than two books quote it, so value is unread rather than zero.", value.Text);
    }

    [Fact]
    public void Twenty_nine_minutes_is_fresh()
    {
        var age = Checks.Age(Offer(TimeSpan.FromMinutes(29)), Now, Actions);

        Assert.Equal(DeskState.Positive, age.State);
        Assert.Equal("Price captured 29 min ago.", age.Text);
        Assert.Null(age.Remedy);
    }

    [Fact]
    public void Thirty_minutes_is_stale_and_offers_the_pull()
    {
        var age = Checks.Age(Offer(TimeSpan.FromMinutes(30)), Now, Actions);

        Assert.Equal(DeskState.Warning, age.State);
        Assert.Equal("Price captured 30 min ago; the line may have moved since.", age.Text);
        Assert.Equal("Pull NFL lines · 2 credits", age.Remedy);
        Assert.NotNull(age.Act);
    }

    [Fact]
    public void Hours_read_as_hours_and_minutes()
    {
        var age = Checks.Age(Offer(new TimeSpan(2, 14, 0)), Now, Actions);
        Assert.Equal("Price captured 2h 14m ago; the line may have moved since.", age.Text);
    }

    [Fact]
    public void A_stale_price_without_a_pull_to_offer_has_no_remedy()
    {
        var age = Checks.Age(Offer(TimeSpan.FromHours(1)), Now, new CheckActions());
        Assert.Null(age.Remedy);
        Assert.Null(age.Act);
    }

    [Fact]
    public void A_closing_line_replaces_the_age_row_and_blocks()
    {
        var rows = Checks.Build(Offer(closing: true), Now.AddMinutes(-42), Now, true, null, 0m, Actions);

        Assert.Equal(3, rows.Count);
        Assert.Equal(DeskState.Negative, rows[1].State);
        Assert.Equal("Closing line: the game started 42 min ago, so this price cannot have been taken.", rows[1].Text);
        Assert.Equal(Checks.OpenJournal, rows[1].Remedy);
        Assert.DoesNotContain(rows, r => r.Text.StartsWith("Price captured", StringComparison.Ordinal));
    }

    [Fact]
    public void A_prior_bet_on_the_side_is_named_with_its_consequence()
    {
        var prior = new JournalEntry
        {
            GameId = 7,
            Market = Markets.Moneyline,
            Outcome = "Kansas City Chiefs",
            Book = "draftkings",
            PriceTaken = -130,
            Stake = 50m,
            PlacedAt = Now.AddHours(-1)
        };

        var duplicate = Checks.Duplicate(true, prior, Actions);

        Assert.Equal(DeskState.Warning, duplicate.State);
        Assert.StartsWith($"Already in the journal: {50m:C0} on ML · Chiefs at -130 (draftkings), ", duplicate.Text);
        Assert.EndsWith(". Logging again records a second bet.", duplicate.Text);
        Assert.Equal(Checks.OpenJournal, duplicate.Remedy);
    }

    [Fact]
    public void An_unread_journal_says_so_rather_than_clearing_the_side()
    {
        var duplicate = Checks.Duplicate(false, null, Actions);
        Assert.Equal(DeskState.Neutral, duplicate.State);
    }

    [Fact]
    public void A_rung_at_another_book_is_that_books_price_and_age()
    {
        var captured = Now.AddMinutes(-40);
        var best = new LineOps.Data.CrossReference.BestOffer("Kansas City Chiefs", "draftkings", -128, null, Now.AddMinutes(-12),
            null, false, false,
            [
                new LineOps.Data.CrossReference.BookPrice("draftkings", -128, null, 0.56) { Ev = 0.027 },
                new LineOps.Data.CrossReference.BookPrice("fanduel", -135, null, 0.574) { Ev = -0.01, CapturedAt = captured }
            ])
        { Fair = Pinnacle, Ev = 0.027 };

        var rung = WagerOffer.AtBook(Markets.Moneyline, best, "FanDuel")!;

        Assert.Equal(-135, rung.PriceAmerican);
        Assert.Equal("fanduel", rung.Book);
        Assert.Equal(captured, rung.CapturedAt);
        Assert.Equal(-0.01, rung.Ev);
        Assert.False(rung.IsBest);
        Assert.Null(WagerOffer.AtBook(Markets.Moneyline, best, "caesars"));
    }
}
