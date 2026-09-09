using Bunit;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Desk;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// One market's best offer, and where the rest of the market stands. The rail is the point:
/// a tick per book, the best one marked, and a label that says whether shopping repays the
/// work — including when the thing that varies is the line rather than the price.
/// </summary>
public class PriceCellTests : DeskTestContext
{
    private static BookPrice Rung(string book, int american, double implied, decimal? line = null)
        => new(book, american, line, implied);

    /// <summary>Three hours before the sample close was captured, so a lead time is assertable.</summary>
    private static readonly DateTimeOffset Captured = new(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Captured.AddHours(3);

    private static BestOffer Offer(
        string outcome = "home",
        string book = "draftkings",
        int american = -110,
        decimal? line = null,
        double? edgePoints = null,
        bool linesVary = false,
        bool isClosing = false,
        params BookPrice[] rungs)
        => new(outcome, book, american, line, isClosing ? Captured : DateTimeOffset.UnixEpoch,
            edgePoints, linesVary, isClosing, rungs);

    [Fact]
    public void No_offer_reads_as_a_dash()
    {
        var cut = RenderComponent<PriceCell>();

        Assert.Equal("—", cut.Find("span.dim").TextContent);
        Assert.Empty(cut.FindAll(".price"));
    }

    [Fact]
    public void Renders_the_price_and_the_books_monogram()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(american: 145, rungs: Rung("draftkings", 145, 0.408))));

        Assert.Equal("+145", cut.Find(".price__odds").TextContent);
        Assert.Equal("DK", cut.Find(".price__book").TextContent);
    }

    /// <summary>
    /// Two prices sit next to each other in every market, and a home-first convention the
    /// reader cannot see is not a convention. Taking the wrong side of +331 / -380 is not a
    /// cosmetic mistake.
    /// </summary>
    [Fact]
    public void Names_the_side_it_is_quoting_when_the_caller_supplies_one()
    {
        var cut = RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, Offer(american: 331, rungs: Rung("draftkings", 331, 0.232)))
            .Add(x => x.Selection, "LAL"));

        Assert.Equal("LAL", cut.Find(".price__sel").TextContent);
    }

    [Fact]
    public void Says_nothing_about_the_side_when_the_caller_supplies_none()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(rungs: Rung("draftkings", -110, 0.524))));

        Assert.Empty(cut.FindAll(".price__sel"));
    }

    [Theory]
    [InlineData("fanduel", "FD")]
    [InlineData("bet365", "B3")]
    [InlineData("betmgm", "MG")]
    [InlineData("caesars", "CZ")]
    [InlineData("pointsbet", "PB")]
    [InlineData("novelbook", "NO")]
    public void An_unknown_book_still_gets_a_two_character_mark(string book, string expected)
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(book: book, rungs: Rung(book, -110, 0.524))));

        Assert.Equal(expected, cut.Find(".price__book").TextContent);
    }

    [Fact]
    public void A_handicap_is_signed_and_a_total_is_not()
    {
        var spread = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(outcome: "home", line: 3.5m, rungs: Rung("draftkings", -110, 0.524, 3.5m))));

        var total = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(outcome: "over", line: 223m, rungs: Rung("draftkings", -110, 0.524, 223m))));

        Assert.Equal("+3.5", spread.Find(".price__line").TextContent);
        Assert.Equal("o223", total.Find(".price__line").TextContent);
    }

    [Fact]
    public void A_lone_book_has_nothing_to_shop_so_it_draws_no_rail()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(rungs: Rung("draftkings", -110, 0.524))));

        Assert.Empty(cut.FindAll(".rail"));
        Assert.Empty(cut.FindAll(".price__edge"));
    }

    [Fact]
    public void Every_book_gets_a_tick_and_the_best_one_is_marked()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(book: "fanduel", edgePoints: 1.2, rungs:
            [
                Rung("draftkings", -115, 0.535),
                Rung("fanduel", -105, 0.512),
                Rung("betmgm", -110, 0.524)
            ])));

        Assert.Equal(3, cut.FindAll(".rail__tick").Count);
        Assert.Single(cut.FindAll(".rail__tick--best"));
    }

    /// <summary>
    /// 0% at the best implied price, 100% at the worst — so the width of the group is the
    /// value of shopping. Written invariant so a comma-decimal machine still emits valid CSS.
    /// </summary>
    [Fact]
    public void Ticks_are_placed_between_the_best_and_the_worst()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(book: "fanduel", edgePoints: 1.2, rungs:
            [
                Rung("fanduel", -105, 0.50),
                Rung("betmgm", -110, 0.55),
                Rung("draftkings", -115, 0.60)
            ])));

        var lefts = cut.FindAll(".rail__tick")
            .Select(t => t.GetAttribute("style"))
            .ToList();

        Assert.Equal("left:0%", lefts[0]);
        Assert.Equal("left:50%", lefts[1]);
        Assert.Equal("left:100%", lefts[2]);
    }

    [Fact]
    public void Books_quoting_the_same_number_all_land_on_the_left()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(rungs:
            [
                Rung("draftkings", -110, 0.524),
                Rung("fanduel", -110, 0.524)
            ])));

        Assert.All(cut.FindAll(".rail__tick"), t => Assert.Equal("left:0%", t.GetAttribute("style")));
    }

    [Fact]
    public void Books_that_agree_say_so()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(edgePoints: 0, rungs:
            [
                Rung("draftkings", -110, 0.524),
                Rung("fanduel", -110, 0.524)
            ])));

        Assert.Equal("books agree", cut.Find(".price__edge").TextContent);
    }

    [Fact]
    public void A_real_gap_is_priced_against_the_worst_on_offer()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(edgePoints: 1.4, rungs:
            [
                Rung("draftkings", -115, 0.535),
                Rung("fanduel", -105, 0.512)
            ])));

        Assert.Equal("+1.4 pts vs worst", cut.Find(".price__edge").TextContent);
    }

    /// <summary>
    /// A differing line outranks the price gap — books quoting -110 on totals of 7, 8 and 9
    /// have a price spread of zero and the largest real difference on the board.
    /// </summary>
    [Fact]
    public void A_varying_line_is_reported_as_such_not_folded_into_the_price_gap()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(outcome: "over", line: 7m, linesVary: true, edgePoints: 0, rungs:
            [
                Rung("draftkings", -110, 0.524, 7m),
                Rung("fanduel", -110, 0.524, 8m)
            ])));

        var edge = cut.Find(".price__edge");

        Assert.Equal("line varies", edge.TextContent);
        Assert.Contains("price__edge--wide", edge.GetAttribute("class"));
    }

    [Fact]
    public void A_varying_line_with_a_gap_reports_both()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(outcome: "over", line: 7m, linesVary: true, edgePoints: 1.5, rungs:
            [
                Rung("draftkings", -120, 0.545, 7m),
                Rung("fanduel", -105, 0.512, 8m)
            ])));

        Assert.Equal("line varies · +1.5 pts", cut.Find(".price__edge").TextContent);
    }

    [Fact]
    public void A_wide_gap_is_flagged_even_when_the_lines_agree()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(edgePoints: 2.0, rungs:
            [
                Rung("draftkings", -130, 0.565),
                Rung("fanduel", -105, 0.512)
            ])));

        Assert.Contains("price__edge--wide", cut.Find(".price__edge").GetAttribute("class"));
    }

    [Fact]
    public void A_narrow_gap_is_not_flagged()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(edgePoints: 0.4, rungs:
            [
                Rung("draftkings", -113, 0.531),
                Rung("fanduel", -110, 0.524)
            ])));

        Assert.DoesNotContain("price__edge--wide", cut.Find(".price__edge").GetAttribute("class") ?? "");
    }

    /// <summary>The tooltip is the whole market, so hovering answers "who else?" without a click.</summary>
    [Fact]
    public void The_tooltip_lists_every_book()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(outcome: "over", line: 7m, edgePoints: 0.5, rungs:
            [
                Rung("draftkings", -110, 0.524, 7m),
                Rung("fanduel", 105, 0.488, 7.5m)
            ])));

        var tooltip = cut.Find(".price").GetAttribute("title") ?? string.Empty;

        Assert.Contains("DK o7 -110", tooltip);
        Assert.Contains("FD o7.5 +105", tooltip);
    }

    // ---- The closing line ---------------------------------------------------------------
    //
    // ADR 0010 deletes the scan tier once a game starts and keeps one closing line per book.
    // The board read only the scans, so a game in play went blank — sixteen empty rows on a
    // nineteen-game slate. The close is the number worth having: it is what the market
    // concluded and what CLV is measured against. It stays, muted, and says what it is.

    [Fact]
    public void A_closing_price_is_shown_rather_than_dropped()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(american: -108, isClosing: true, rungs: Rung("draftkings", -108, 0.519))));

        Assert.Equal("-108", cut.Find(".price__odds").TextContent);
        Assert.Empty(cut.FindAll("span.dim"));
    }

    [Fact]
    public void A_closing_price_is_muted_so_it_is_not_mistaken_for_a_live_one()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524))));

        Assert.Contains("price--closing", cut.Find(".price").GetAttribute("class"));
    }

    [Fact]
    public void A_live_price_carries_no_closing_treatment()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(rungs: Rung("draftkings", -110, 0.524))));

        Assert.DoesNotContain("price--closing", cut.Find(".price").GetAttribute("class") ?? "");
    }

    /// <summary>
    /// A lone book leaves no rail, and the rail is what would otherwise have carried the fact.
    /// So the cell says it in a word rather than rendering as an ordinary price.
    /// </summary>
    [Fact]
    public void A_lone_closing_book_still_says_the_market_is_closed()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524))));

        Assert.Equal("closed", cut.Find(".price__edge").TextContent);
    }

    /// <summary>
    /// "+1.4 pts vs worst" on a finished game invites moving to a book that is no longer
    /// quoting it. The gap is still reported — it is real — but as where the books finished.
    /// </summary>
    [Fact]
    public void A_closed_market_reports_its_gap_as_history_not_as_an_opportunity()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(isClosing: true, edgePoints: 1.4, rungs:
            [
                Rung("draftkings", -115, 0.535),
                Rung("fanduel", -105, 0.512)
            ])));

        var edge = cut.Find(".price__edge");

        Assert.Equal("+1.4 pts vs worst at close", edge.TextContent);

        // And never flagged as wide: "wide" is a call to go and shop, and there is nothing
        // left to shop.
        Assert.DoesNotContain("price__edge--wide", edge.GetAttribute("class") ?? "");
    }

    /// <summary>
    /// Mud only puts a tooltip's body in the DOM once it is shown, so the assertion is against
    /// the component's own text rather than rendered markup. That is the thing under test
    /// anyway: what the reader is told when they hover a number they cannot take.
    /// </summary>
    [Fact]
    public void A_closing_price_explains_itself_and_says_how_early_it_was_taken()
    {
        var cut = RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524)))
            .Add(x => x.StartsAt, Start));

        var why = cut.FindComponent<MudTooltip>().Instance.Text;

        Assert.Contains("closing price", why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3.0h before start", why);

        // The book breakdown the live cell puts in a title attribute rides along, because a
        // native tooltip cannot hold a sentence and a table.
        Assert.Contains("DK -110", why);
    }

    [Fact]
    public void Without_a_start_time_the_closing_price_still_says_what_it_is()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524))));

        var why = cut.FindComponent<MudTooltip>().Instance.Text;

        Assert.Contains("closing price", why, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("before start", why);
    }

    /// <summary>
    /// The live cell keeps its native title attribute — one line of book breakdown is exactly
    /// what that is for, and putting a MudBlazor component in nineteen rows × six columns to
    /// say the same thing would cost the board a great deal for no gain.
    /// </summary>
    [Fact]
    public void A_live_price_keeps_the_native_tooltip_and_grows_no_second_one()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(rungs: Rung("draftkings", -110, 0.524))));

        Assert.NotNull(cut.Find(".price").GetAttribute("title"));
        Assert.Empty(cut.FindComponents<MudTooltip>());
    }

    /// <summary>The muted cell must not also carry the native tooltip, or the two compete.</summary>
    [Fact]
    public void A_closing_price_drops_the_native_tooltip_the_rendered_one_replaces()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer,
            Offer(isClosing: true, rungs: Rung("draftkings", -110, 0.524))));

        Assert.Null(cut.Find(".price").GetAttribute("title"));
    }
}
