using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The close as ESPN's odds endpoint states it, for a game whose summary carries no odds block.
///
/// <para>
/// The payload is the real shape returned for event 401772510 (Cowboys at Eagles, 4 September
/// 2025), trimmed to the fields that are read. Its summary's <c>pickcenter</c> is empty, as it is
/// for every 2025 NFL game before late November; this endpoint still has the open and the close.
/// Two providers answer: the pre-game book and its live feed, and only the first is a close.
/// </para>
/// </summary>
public class EspnCoreOddsTests
{
    private const string Payload = """
    {
      "count": 2,
      "items": [
        {
          "provider": { "id": "58", "name": "ESPN BET", "priority": 0 },
          "overUnder": 47.5,
          "spread": -7.5,
          "overOdds": -130.0,
          "underOdds": 100.0,
          "awayTeamOdds": {
            "moneyLine": 300,
            "spreadOdds": -140.0,
            "open": {
              "pointSpread": { "american": "+4.5" },
              "spread": { "american": "+130" },
              "moneyLine": { "american": "+230" }
            },
            "close": {
              "pointSpread": { "alternateDisplayValue": "+7.5", "american": "+7.5" },
              "spread": { "alternateDisplayValue": "-110", "american": "-110" },
              "moneyLine": { "alternateDisplayValue": "+300", "american": "+300" }
            }
          },
          "homeTeamOdds": {
            "moneyLine": -400,
            "spreadOdds": 110.0,
            "close": {
              "pointSpread": { "alternateDisplayValue": "-7.5", "american": "-7.5" },
              "spread": { "alternateDisplayValue": "-110", "american": "-110" },
              "moneyLine": { "alternateDisplayValue": "-400", "american": "-400" }
            }
          },
          "open": {
            "over": { "american": "-150" },
            "under": { "american": "+130" },
            "total": { "american": "43.5" }
          },
          "close": {
            "over": { "alternateDisplayValue": "-110", "american": "-110" },
            "under": { "alternateDisplayValue": "-110", "american": "-110" },
            "total": { "alternateDisplayValue": "47.5", "american": "47.5" }
          },
          "current": {
            "over": { "american": "-130" },
            "under": { "american": "+100" },
            "total": { "american": "47.5" }
          }
        },
        {
          "provider": { "id": "59", "name": "ESPN Bet - Live Odds", "priority": 0 },
          "overUnder": 44.5,
          "awayTeamOdds": { "current": { "pointSpread": { "american": "+4.5" } } },
          "homeTeamOdds": { "current": { "pointSpread": { "american": "-4.5" } } },
          "close": {
            "over": { "american": "+160" },
            "under": { "american": "-250" },
            "total": { "american": "44.5" }
          }
        }
      ]
    }
    """;

    private static readonly DateTimeOffset Kickoff = new(2025, 9, 5, 0, 20, 0, TimeSpan.Zero);

    private static List<Core.Contracts.CanonicalOdds> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        return EspnStatsAdapter.ParseCoreOdds(
            doc.RootElement, "401772510", "Philadelphia Eagles", "Dallas Cowboys", Kickoff).ToList();
    }

    [Fact]
    public void Every_market_is_read_from_the_pre_game_book()
    {
        var lines = Parse(Payload);

        Assert.Equal(6, lines.Count);
        Assert.All(lines, l => Assert.Equal("ESPN BET", l.Book));
        Assert.All(lines, l => Assert.Equal(Kickoff, l.CapturedAt));
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Moneyline));
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Spread));
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Total));
    }

    /// <summary>
    /// The total is the close — 47.5 at -110 a side — not the open (43.5), the last live quote on
    /// the item (-130/+100), or the live feed's 44.5 at +160.
    /// </summary>
    [Fact]
    public void The_total_is_the_closing_number_at_its_closing_prices()
    {
        var total = Parse(Payload).Where(l => l.Market == Markets.Total).ToList();

        Assert.All(total, l => Assert.Equal(47.5m, l.Line));
        Assert.Equal(-110, total.Single(l => l.Outcome == "over").PriceAmerican);
        Assert.Equal(-110, total.Single(l => l.Outcome == "under").PriceAmerican);
    }

    [Fact]
    public void Team_markets_are_named_by_team_at_their_closing_numbers()
    {
        var lines = Parse(Payload);

        Assert.Contains(lines, l => l is { Market: Markets.Moneyline, Outcome: "Philadelphia Eagles", PriceAmerican: -400, Line: null });
        Assert.Contains(lines, l => l is { Market: Markets.Moneyline, Outcome: "Dallas Cowboys", PriceAmerican: 300 });
        Assert.Contains(lines, l => l is { Market: Markets.Spread, Outcome: "Philadelphia Eagles", Line: -7.5m, PriceAmerican: -110 });
        Assert.Contains(lines, l => l is { Market: Markets.Spread, Outcome: "Dallas Cowboys", Line: 7.5m, PriceAmerican: -110 });
    }

    /// <summary>A live feed is the number in play; with nothing else answering, there is no close.</summary>
    [Fact]
    public void A_live_feed_alone_is_not_a_close()
    {
        var liveOnly = """
        {
          "items": [{
            "provider": { "name": "ESPN Bet - Live Odds" },
            "close": { "over": { "american": "+160" }, "under": { "american": "-250" }, "total": { "american": "44.5" } }
          }]
        }
        """;

        Assert.Empty(Parse(liveOnly));
    }

    [Fact]
    public void No_items_and_no_close_yield_nothing()
    {
        Assert.Empty(Parse("""{ "count": 0, "items": [] }"""));
        Assert.Empty(Parse("""{ "items": [{ "provider": { "name": "ESPN BET" }, "current": {} }] }"""));
    }

    /// <summary>A pick'em closes at a spread of zero, written in words.</summary>
    [Fact]
    public void A_pickem_spread_closes_at_zero()
    {
        var pickem = """
        {
          "items": [{
            "provider": { "name": "ESPN BET" },
            "homeTeamOdds": { "close": { "pointSpread": { "american": "PK" }, "spread": { "american": "-110" } } }
          }]
        }
        """;

        var line = Assert.Single(Parse(pickem));
        Assert.Equal(Markets.Spread, line.Market);
        Assert.Equal(0m, line.Line);
    }
}
