using LineOps.Core.Entities;
using LineOps.Ingestion.Services;
using LineOps.Web.Components.Panels;

namespace LineOps.Web.Tests;

/// <summary>
/// What the Journal's edit form says about a field, beside the field. A price inside the dead
/// band is not a price any book quotes, a stake has to be money, and a spread or a total with
/// no number cannot be graded. Pure functions, so the rules are pinned here and the form only
/// decides when to show them.
/// </summary>
public class JournalValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-99)]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(50)]
    public void A_price_inside_the_dead_band_says_what_a_price_is(int price)
        => Assert.Equal("A price is −100 or lower, or +100 or higher.", JournalPanel.PriceError(price));

    [Theory]
    [InlineData(-100)]
    [InlineData(100)]
    [InlineData(-110)]
    [InlineData(250)]
    public void A_quoted_price_has_no_error(int price)
        => Assert.Null(JournalPanel.PriceError(price));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_stake_of_nothing_asks_for_one(decimal stake)
        => Assert.Equal("Enter a stake above zero.", JournalPanel.StakeError(stake));

    [Fact]
    public void A_stake_above_zero_has_no_error()
        => Assert.Null(JournalPanel.StakeError(0.5m));

    [Theory]
    [InlineData(Markets.Spread)]
    [InlineData(Markets.Total)]
    public void A_line_market_with_no_line_asks_for_it(string market)
        => Assert.Equal("Enter the line.", JournalPanel.LineError(market, null));

    [Theory]
    [InlineData(-0.5)]
    [InlineData(0)]
    public void A_total_at_or_below_zero_says_a_total_is_above_zero(double line)
        => Assert.Equal("A total is above zero.", JournalPanel.LineError(Markets.Total, (decimal)line));

    [Theory]
    [InlineData(Markets.Spread, -3.5)]
    [InlineData(Markets.Spread, 0)]
    [InlineData(Markets.Total, 8.5)]
    public void A_line_that_can_be_graded_has_no_error(string market, double line)
        => Assert.Null(JournalPanel.LineError(market, (decimal)line));

    [Fact]
    public void A_moneyline_needs_no_line()
        => Assert.Null(JournalPanel.LineError(Markets.Moneyline, null));

    private static readonly EntryDraft Opened =
        new(7, Markets.Spread, null, "Yankees", -1.5m, -110, 100m, "fanduel", null);

    [Fact]
    public void An_entry_as_it_was_opened_is_not_dirty()
        => Assert.False(JournalPanel.EntryIsDirty(Opened with { }, Opened));

    /// <summary>
    /// A note box cleared back to nothing hands the form an empty string where the entry held
    /// null; that is the same note, and asking to discard it would be crying wolf.
    /// </summary>
    [Fact]
    public void An_emptied_note_is_the_same_as_no_note()
        => Assert.False(JournalPanel.EntryIsDirty(Opened with { Note = "" }, Opened));

    /// <summary>ManualPrice follows the price and the line; it is not a change of its own.</summary>
    [Fact]
    public void The_manual_flag_alone_is_not_a_change()
        => Assert.False(JournalPanel.EntryIsDirty(Opened with { ManualPrice = true }, Opened));

    [Theory]
    [MemberData(nameof(Edits))]
    public void Any_edited_field_makes_the_entry_dirty(EntryDraft edited)
        => Assert.True(JournalPanel.EntryIsDirty(edited, Opened));

    public static TheoryData<EntryDraft> Edits() =>
    [
        Opened with { GameId = 8 },
        Opened with { Market = Markets.Moneyline },
        Opened with { Outcome = "Red Sox" },
        Opened with { LineTaken = -2.5m },
        Opened with { PriceTaken = -105 },
        Opened with { Stake = 50m },
        Opened with { Book = "draftkings" },
        Opened with { Note = "Closing-line chase" }
    ];
}
