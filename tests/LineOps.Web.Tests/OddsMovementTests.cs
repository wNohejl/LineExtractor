using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using Row = LineOps.Web.Components.Panels.OddsPanel.MovementRow;

namespace LineOps.Web.Tests;

/// <summary>
/// The Line movement window's arithmetic, without a database: what a move is, which way it
/// went and how far, and what the window says about it. Prices are written only when they
/// change, so a book's first row is where it opened and every later row is a move.
/// </summary>
public class OddsMovementTests
{
    // Noon Eastern on Oct 24, 2026 (still daylight time), so no reading falls across a day.
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 16, 0, 0, TimeSpan.Zero);

    private const string Away = "Dallas Cowboys";
    private const string Home = "Philadelphia Eagles";

    private static OddsSnapshot Snap(string book, string outcome, int price, double hours, decimal? line = null, string market = Markets.Moneyline)
        => new()
        {
            Book = book,
            Market = market,
            Outcome = outcome,
            Line = line,
            PriceAmerican = price,
            CapturedAt = T0.AddHours(hours)
        };

    private static Row Only(IEnumerable<OddsSnapshot> snapshots, string outcome = Away)
        => Assert.Single(OddsPanel.MovementRows(snapshots, outcome));

    [Theory]
    [InlineData(105, 5)]
    [InlineData(-105, -5)]
    [InlineData(-110, -10)]
    [InlineData(100, 0)]
    [InlineData(-100, 0)]
    [InlineData(136, 36)]
    [InlineData(-218, -118)]
    public void Cents_close_the_gap_at_even_money(int american, int cents)
        => Assert.Equal(cents, OddsPanel.Cents(american));

    [Fact]
    public void A_stored_zero_has_no_place_on_the_scale()
        => Assert.Null(OddsPanel.Cents(0));

    /// <summary>One reading is where the book opened, not a move: no trend, no last move, "unmoved".</summary>
    [Fact]
    public void A_book_seen_once_is_unmoved()
    {
        var row = Only([Snap("draftkings", Away, -110, 0)]);

        Assert.Equal(0, row.Moves);
        Assert.Single(row.Trend);
        Assert.Null(row.LastMoved);
        Assert.False(row.Moved);
        Assert.Equal("unmoved", OddsPanel.MoveText(row));
        Assert.Equal("Price -110, seen once, no moves", OddsPanel.TrendLabel(row));
    }

    [Fact]
    public void Every_row_after_the_first_is_a_move()
    {
        var row = Only(
        [
            Snap("draftkings", Away, 136, 0),
            Snap("draftkings", Away, 120, 1),
            Snap("draftkings", Away, -218, 2)
        ]);

        Assert.Equal(2, row.Moves);
        Assert.Equal(136, row.Open.PriceAmerican);
        Assert.Equal(-218, row.Now.PriceAmerican);
        Assert.Equal(T0.AddHours(2), row.LastMoved);
        Assert.Equal(-154, row.PriceCents);
        Assert.Equal("↓ 154¢", OddsPanel.MoveText(row));
        Assert.Equal("Price +136 to -218, 2 moves", OddsPanel.TrendLabel(row));
    }

    /// <summary>The other side is the mirror; the rows are the chosen side's alone, a row per book.</summary>
    [Fact]
    public void Only_the_side_in_focus_is_read()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("fanduel", Away, 140, 0),
            Snap("draftkings", Away, 136, 0),
            Snap("draftkings", Home, -160, 0),
            Snap("draftkings", Away, 130, 1),
            Snap("draftkings", Home, -152, 1)
        ], Away);

        Assert.Equal(["draftkings", "fanduel"], rows.Select(r => r.Book));
        Assert.All(rows, r => Assert.Equal(Away, r.Now.Outcome));
        Assert.Equal(1, rows[0].Moves);
    }

    /// <summary>A spread moves its number and its price: points first, then cents with the direction.</summary>
    [Fact]
    public void A_spread_row_reports_the_line_and_the_price()
    {
        var row = Only(
        [
            Snap("draftkings", Away, -104, 0, 4.5m, Markets.Spread),
            Snap("draftkings", Away, -110, 3, 6m, Markets.Spread)
        ]);

        Assert.True(row.TrendsLine);
        Assert.Equal(1.5m, row.LineChange);
        Assert.Equal("+1.5 pts · ↓ 6¢", OddsPanel.MoveText(row));
        Assert.Equal("+4.5 -104", OddsPanel.Quote(row.Open));
        Assert.Equal("+6 -110", OddsPanel.Quote(row.Now));
        Assert.Equal("Line +4.5 to +6; price -104 to -110, shortened 6¢", OddsPanel.MoveTitle(row, Markets.Spread));
    }

    /// <summary>When only the juice moved, the sparkline draws the price: a flat line beside "6¢" would say nothing happened.</summary>
    [Fact]
    public void A_handicap_whose_number_held_draws_its_price()
    {
        var row = Only(
        [
            Snap("fanduel", "Over", -110, 0, 47.5m, Markets.Total),
            Snap("fanduel", "Over", -104, 1, 47.5m, Markets.Total)
        ], "Over");

        Assert.False(row.TrendsLine);
        Assert.Equal([-10, -4], row.Trend);
        Assert.Equal("↑ 6¢", OddsPanel.MoveText(row));
        Assert.Equal("o47.5 -104", OddsPanel.Quote(row.Now));
    }

    [Fact]
    public void One_point_is_a_point_not_points()
    {
        var row = Only(
        [
            Snap("draftkings", Away, -110, 0, -3m, Markets.Spread),
            Snap("draftkings", Away, -110, 1, -4m, Markets.Spread)
        ]);

        Assert.Equal("-1 pt", OddsPanel.MoveText(row));
    }

    /// <summary>Moved and came back is a different fact from never moving, and says so.</summary>
    [Fact]
    public void A_round_trip_is_no_net_move()
    {
        var row = Only(
        [
            Snap("draftkings", Away, -110, 0),
            Snap("draftkings", Away, -120, 1),
            Snap("draftkings", Away, -110, 2)
        ]);

        Assert.Equal(2, row.Moves);
        Assert.False(row.Moved);
        Assert.Equal("no net move", OddsPanel.MoveText(row));
    }

    /// <summary>
    /// Every sparkline in the table is on one time axis, first reading anywhere to the last, and
    /// a book's last price holds to the right edge — so a move at the same hour lines up.
    /// </summary>
    [Fact]
    public void Trends_share_one_time_axis_and_hold_to_its_end()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("draftkings", Away, -110, 0),
            Snap("draftkings", Away, -120, 2),
            Snap("fanduel", Away, -105, 0),
            Snap("fanduel", Away, -115, 4)
        ], Away);

        var dk = rows.Single(r => r.Book == "draftkings");
        var fd = rows.Single(r => r.Book == "fanduel");

        Assert.Equal([0, 0.5, 1], dk.Positions!);
        Assert.Equal([-10, -20, -20], dk.Trend);
        Assert.Equal([0, 1], fd.Positions!);
        Assert.Equal([-5, -15], fd.Trend);
    }

    /// <summary>A stored zero is a broken reading: shown as one, and never counted as a move of hundreds of cents.</summary>
    [Fact]
    public void A_broken_price_is_left_out_of_the_trend_and_the_move()
    {
        var row = Only(
        [
            Snap("draftkings", Away, -110, 0),
            Snap("draftkings", Away, 0, 1)
        ]);

        Assert.Equal([-10], row.Trend);
        Assert.Null(row.PriceCents);
        Assert.Equal("—", OddsPanel.Quote(row.Now));
    }

    [Fact]
    public void The_headline_names_the_biggest_mover_in_the_desks_words()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("draftkings", Away, 136, 0),
            Snap("draftkings", Away, -218, 1),
            Snap("fanduel", Away, 140, 0),
            Snap("fanduel", Away, 130, 1)
        ], Away);

        Assert.Equal("Dallas Cowboys shortened +136 to -218 at draftkings",
            OddsPanel.Headline(rows, Markets.Moneyline, Away));
    }

    [Fact]
    public void Nothing_moving_is_also_a_finding()
    {
        var rows = OddsPanel.MovementRows([Snap("draftkings", Away, -110, 0), Snap("fanduel", Away, -105, 0)], Away);

        Assert.Equal("Every book is holding its opening number", OddsPanel.Headline(rows, Markets.Moneyline, Away));
    }

    /// <summary>A line move changes what the bet is, so it outranks a larger price move elsewhere.</summary>
    [Fact]
    public void A_line_move_outranks_a_price_move()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("draftkings", Away, -110, 0, 4.5m, Markets.Spread),
            Snap("draftkings", Away, -150, 1, 4.5m, Markets.Spread),
            Snap("fanduel", Away, -110, 0, 4.5m, Markets.Spread),
            Snap("fanduel", Away, -110, 1, 3m, Markets.Spread)
        ], Away);

        Assert.Equal("Dallas Cowboys moved +4.5 to +3 at fanduel", OddsPanel.Headline(rows, Markets.Spread, Away));
    }

    /// <summary>Over and Under share one number, so a total's move is the total's, unsigned.</summary>
    [Fact]
    public void A_totals_move_belongs_to_the_total()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("fanduel", "Over", -110, 0, 47.5m, Markets.Total),
            Snap("fanduel", "Over", -110, 1, 46m, Markets.Total)
        ], "Over");

        Assert.Equal("The total moved 47.5 to 46 at fanduel", OddsPanel.Headline(rows, Markets.Total, "Over"));
    }

    [Fact]
    public void Sides_read_away_first_and_over_first()
    {
        Assert.Equal([Away, Home], OddsPanel.Sides([Home, Away, Home], Markets.Moneyline, Away, Home));
        Assert.Equal(["Over", "Under"], OddsPanel.Sides(["Under", "Over"], Markets.Total, Away, Home));
    }

    [Fact]
    public void The_span_line_gives_the_days_and_the_moves()
    {
        var rows = OddsPanel.MovementRows(
        [
            Snap("draftkings", Away, -110, -96),
            Snap("draftkings", Away, -120, 0),
            Snap("fanduel", Away, -105, -48),
            Snap("fanduel", Away, -115, 0)
        ], Away);

        Assert.Equal("Oct 20 to Oct 24 · 2 moves", OddsPanel.SpanText(rows));
    }

    [Fact]
    public void A_single_day_gives_its_hours()
    {
        var rows = OddsPanel.MovementRows([Snap("draftkings", Away, -110, 1), Snap("draftkings", Away, -120, 3.5)], Away);

        Assert.Equal("Oct 24 13:00 to 15:30 ET · 1 move", OddsPanel.SpanText(rows));
    }

    [Fact]
    public void A_single_reading_says_so()
    {
        var rows = OddsPanel.MovementRows([Snap("draftkings", Away, -110, 0)], Away);

        Assert.Equal("Seen once, Oct 24 12:00 ET · no moves yet", OddsPanel.SpanText(rows));
    }

    [Fact]
    public void Last_moved_is_relative()
    {
        Assert.Equal("2.0d ago", OddsPanel.Ago(T0, T0.AddDays(2)));
        Assert.Equal("just now", OddsPanel.Ago(T0, T0.AddSeconds(20)));
    }
}
