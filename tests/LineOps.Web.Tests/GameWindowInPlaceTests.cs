using System.Text.RegularExpressions;
using Bunit;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Panels;
using LineOps.Web.Components.Snippets;
using LineOps.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Game window answers its follow-up questions in place (spec 2026-11-01): a market opens
/// under its row instead of a Line movement window, head to head opens under the title instead
/// of a third window, and the header holds only what applies to every tab.
/// </summary>
public class GameWindowInPlaceTests : DeskTestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);

    public GameWindowInPlaceTests()
    {
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(new JournalParlayTests.NoDatabase());
    }

    private static string GamePanelSource()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "GamePanel.razor"));

    private static BookPrice Rung(string book, int price, decimal? line = null)
        => new(book, price, line, OddsMath.ImpliedProbability(price));

    /// <summary>A side's offer with its rungs best first, as BoardService ranks them.</summary>
    private static BestOffer Offer(string outcome, params BookPrice[] rungs)
        => new(outcome, rungs[0].Book, rungs[0].PriceAmerican, rungs[0].Line, T0, null, false, true, rungs);

    private static HeadToHeadRow Meeting(bool? homeWon)
        => new(1, T0, "Denver Broncos", "Kansas City Chiefs", 20, 17, GameStatus.Final, true, homeWon, null, null, null, null);

    // ---- Head to head's label ------------------------------------------------------------

    [Fact]
    public void Head_to_head_names_the_leader_first()
    {
        var rows = new[] { Meeting(false), Meeting(false), Meeting(true), Meeting(false), Meeting(false) };

        Assert.Equal("Chiefs 4–1", GameHeadToHead.Summary(rows, "Broncos", "Chiefs"));
        Assert.Equal("Broncos 1–0", GameHeadToHead.Summary([Meeting(true)], "Broncos", "Chiefs"));
    }

    [Fact]
    public void Head_to_head_says_level_and_none()
    {
        Assert.Equal("Level 1–1", GameHeadToHead.Summary([Meeting(true), Meeting(false)], "Broncos", "Chiefs"));
        Assert.Equal("no meetings on record", GameHeadToHead.Summary([], "Broncos", "Chiefs"));
        Assert.Equal("none on record", GameHeadToHead.Summary([], "A", "B", isFight: true));
        Assert.Equal("1 meeting, no result", GameHeadToHead.Summary([Meeting(null)], "Broncos", "Chiefs"));
    }

    /// <summary>Closed, it is one press and carries no meetings; open, it says it is open.</summary>
    [Fact]
    public void Head_to_head_is_a_disclosure_button()
    {
        Services.AddScoped<GameLogService>(_ => throw new InvalidOperationException("no database"));

        var cut = RenderComponent<GameHeadToHead>(p => p
            .Add(c => c.GameId, 7)
            .Add(c => c.HomeName, "Broncos")
            .Add(c => c.AwayName, "Chiefs"));

        var toggle = cut.Find("button.gameh2h__toggle");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".gameh2h__region"));

        toggle.Click();

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("button.gameh2h__toggle").GetAttribute("aria-expanded")));
        var region = cut.Find(".gameh2h__region");
        Assert.Equal(cut.Find("button.gameh2h__toggle").GetAttribute("aria-controls"), region.Id);
        Assert.Contains("Previous meetings did not load.", region.TextContent);
    }

    // ---- A market opened in place --------------------------------------------------------

    [Fact]
    public void Every_book_joins_both_sides_in_name_order()
    {
        var away = Offer("Kansas City Chiefs", Rung("betmgm", -120), Rung("draftkings", -125));
        var home = Offer("Denver Broncos", Rung("draftkings", 105), Rung("fanduel", 102), Rung("betmgm", 100));

        var books = MarketMovement.EveryBook(away, home);

        Assert.Equal(["betmgm", "draftkings", "fanduel"], books.Select(b => b.Book));
        Assert.Null(books[2].First);
        Assert.Equal(105, books[1].Second!.PriceAmerican);
    }

    /// <summary>
    /// Once the game has started the scans are gone (ADR 0010), so the opened market shows every
    /// book's close on both sides, the best on each marked by weight and a dot, and reads nothing.
    /// </summary>
    [Fact]
    public void A_started_game_opens_onto_every_book_s_close()
    {
        var away = Offer("Kansas City Chiefs", Rung("betmgm", -108, -1.5m), Rung("draftkings", -110, -1.5m));
        var home = Offer("Denver Broncos", Rung("draftkings", -110, 1.5m), Rung("betmgm", -112, 1.5m));

        var cut = RenderComponent<MarketMovement>(p => p
            .Add(c => c.GameId, 7)
            .Add(c => c.Market, Markets.Spread)
            .Add(c => c.MarketName, "Spread")
            .Add(c => c.First, away)
            .Add(c => c.Second, home)
            .Add(c => c.FirstSide, "Chiefs")
            .Add(c => c.SecondSide, "Broncos")
            .Add(c => c.Closing, true));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("BetMGM", rows[0].TextContent);
        Assert.Contains("-1.5 -108", rows[0].TextContent.Replace('−', '-'));
        Assert.Equal(2, cut.FindAll(".gamemove__best").Count);
        Assert.Equal(2, cut.FindAll(".gamemove__dot").Count);
        Assert.Empty(cut.FindAll(".gate"));
        Assert.Contains("before the start", cut.Find(".gamemove__foot").TextContent);
    }

    /// <summary>Before the start it reads the scans; a read that fails says so in the row, with Try again.</summary>
    [Fact]
    public void A_pregame_market_reads_its_moves_and_fails_in_place()
    {
        var cut = RenderComponent<MarketMovement>(p => p
            .Add(c => c.GameId, 7)
            .Add(c => c.Market, Markets.Moneyline)
            .Add(c => c.MarketName, "Moneyline")
            .Add(c => c.FirstSide, "Chiefs")
            .Add(c => c.SecondSide, "Broncos")
            .Add(c => c.Closing, false));

        cut.WaitForAssertion(() => Assert.Contains("The moves did not load.", cut.Markup));
        Assert.Equal("Try again", cut.Find(".note button").TextContent.Trim());
    }

    // ---- The Game window's source --------------------------------------------------------

    /// <summary>Movement and Head to head no longer open windows from the header; the season heads its panes.</summary>
    [Fact]
    public void The_header_holds_only_what_applies_to_every_tab()
    {
        var source = GamePanelSource();
        var keys = Regex.Match(source, @"<Keys>(.*?)</Keys>", RegexOptions.Singleline).Groups[1].Value;

        Assert.DoesNotContain("OpenMovement", keys);
        Assert.DoesNotContain("OpenHeadToHead", keys);
        Assert.DoesNotContain("_seasonOptions", keys);

        Assert.Contains("<GameHeadToHead ", source);
        Assert.Equal(2, Regex.Matches(source, @"@RenderSeason\(\)").Count);
    }

    /// <summary>Each market's name is its disclosure, and the opened row is the market in place.</summary>
    [Fact]
    public void A_market_row_discloses_in_place()
    {
        var source = GamePanelSource();

        Assert.Contains("class=\"wgroup__label gamelines__toggle disclosure__summary\"", source);
        Assert.Contains("aria-expanded=\"@(open ? \"true\" : \"false\")\" aria-controls=\"@region\"", source);
        Assert.Contains("<MarketMovement Id=\"@region\"", source);
        Assert.DoesNotContain("Hover a price for every book", source);
    }

    // ---- Your bets -----------------------------------------------------------------------

    private static JournalEntry Bet(EntryResult result, decimal stake)
        => new() { Result = result, Stake = stake, Outcome = "Kansas City Chiefs", Market = Markets.Moneyline, Book = "draftkings" };

    [Fact]
    public void Folded_bets_say_what_is_riding()
    {
        var bets = new[] { Bet(EntryResult.Pending, 110), Bet(EntryResult.Pending, 125), Bet(EntryResult.Pending, 100), Bet(EntryResult.Win, 50) };

        Assert.Equal($"3 pending · {335m:C0} at risk · 1 won", GamePanel.BetsSummary(bets));
    }
}
