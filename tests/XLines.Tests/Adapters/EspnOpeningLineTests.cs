using System.Text.Json;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Ingestion.Adapters;

namespace XLines.Tests.Adapters;

/// <summary>
/// What the line opened at, read beside the close from the same blocks the close comes from, so
/// it costs no request.
///
/// <para>
/// The pickcenter payload is the real shape returned for event 401873053 (Patriots and Lions in
/// Munich, 15 November 2026), trimmed to what is read; the core odds payload is the shape of
/// event 401873070's odds document (Dolphins at Bills), with a close added beside its open as a
/// finished game's document carries one.
/// </para>
/// </summary>
public class EspnOpeningLineTests
{
    private const string Pickcenter = """
    {
      "pickcenter": [
        {
          "provider": { "id": "100", "name": "DraftKings" },
          "moneyline": {
            "home": { "close": { "odds": "-148" }, "open": { "odds": "-125" } },
            "away": { "close": { "odds": "+124" }, "open": { "odds": "+105" } }
          },
          "pointSpread": {
            "home": { "close": { "line": "-2.5", "odds": "-120" }, "open": { "line": "-1.5", "odds": "-110" } },
            "away": { "close": { "line": "+2.5", "odds": "+100" }, "open": { "line": "+1.5", "odds": "-110" } }
          },
          "total": {
            "over":  { "close": { "line": "o50.5", "odds": "-118" }, "open": { "line": "o48.5", "odds": "-110" } },
            "under": { "close": { "line": "u50.5", "odds": "-102" }, "open": { "line": "u48.5", "odds": "-110" } }
          }
        }
      ]
    }
    """;

    private const string CoreOdds = """
    {
      "count": 1,
      "items": [
        {
          "provider": { "id": "100", "name": "DraftKings", "priority": 1 },
          "awayTeamOdds": {
            "open": {
              "pointSpread": { "alternateDisplayValue": "+11.5", "american": "+11.5" },
              "spread": { "alternateDisplayValue": "-110", "american": "-110" },
              "moneyLine": { "alternateDisplayValue": "+440", "american": "+440" }
            },
            "close": {
              "pointSpread": { "alternateDisplayValue": "+16.5", "american": "+16.5" },
              "spread": { "alternateDisplayValue": "-112", "american": "-112" },
              "moneyLine": { "alternateDisplayValue": "+880", "american": "+880" }
            }
          },
          "homeTeamOdds": {
            "open": {
              "pointSpread": { "alternateDisplayValue": "-11.5", "american": "-11.5" },
              "spread": { "alternateDisplayValue": "-110", "american": "-110" },
              "moneyLine": { "alternateDisplayValue": "-600", "american": "-600" }
            },
            "close": {
              "pointSpread": { "alternateDisplayValue": "-16.5", "american": "-16.5" },
              "spread": { "alternateDisplayValue": "-107", "american": "-107" },
              "moneyLine": { "alternateDisplayValue": "-1520", "american": "-1520" }
            }
          },
          "open": {
            "over": { "alternateDisplayValue": "-110", "american": "-110" },
            "under": { "alternateDisplayValue": "-110", "american": "-110" },
            "total": { "alternateDisplayValue": "47.5", "american": "47.5" }
          },
          "close": {
            "over": { "alternateDisplayValue": "-114", "american": "-114" },
            "under": { "alternateDisplayValue": "-105", "american": "-105" },
            "total": { "alternateDisplayValue": "44.5", "american": "44.5" }
          }
        }
      ]
    }
    """;

    private static readonly DateTimeOffset Kickoff = new(2026, 11, 15, 14, 30, 0, TimeSpan.Zero);

    private static List<CanonicalOdds> FromPickcenter(string json)
    {
        using var doc = JsonDocument.Parse(json);

        return EspnStatsAdapter.ParseClosingLines(
            doc.RootElement, "401873053", "Detroit Lions", "New England Patriots", Kickoff).ToList();
    }

    private static List<CanonicalOdds> FromCore(string json)
    {
        using var doc = JsonDocument.Parse(json);

        return EspnStatsAdapter.ParseCoreOdds(
            doc.RootElement, "401873070", "Buffalo Bills", "Miami Dolphins", Kickoff).ToList();
    }

    private static CanonicalOdds Side(List<CanonicalOdds> lines, string market, string outcome)
        => lines.Single(l => l.Market == market && l.Outcome == outcome);

    [Fact]
    public void Pickcenter_opens_every_market_beside_its_close()
    {
        var lines = FromPickcenter(Pickcenter);

        Assert.Equal(6, lines.Count);

        // A moneyline opens at a price and no line.
        Assert.Equal(new CanonicalOpening(null, -125), Side(lines, Markets.Moneyline, "Detroit Lions").Opening);
        Assert.Equal(new CanonicalOpening(null, 105), Side(lines, Markets.Moneyline, "New England Patriots").Opening);

        var homeSpread = Side(lines, Markets.Spread, "Detroit Lions");
        Assert.Equal(-2.5m, homeSpread.Line);
        Assert.Equal(-120, homeSpread.PriceAmerican);
        Assert.Equal(new CanonicalOpening(-1.5m, -110), homeSpread.Opening);
        Assert.Equal(new CanonicalOpening(1.5m, -110), Side(lines, Markets.Spread, "New England Patriots").Opening);

        // The total's open is decorated as its close is, and stripped the same way.
        Assert.Equal(new CanonicalOpening(48.5m, -110), Side(lines, Markets.Total, "over").Opening);
        Assert.Equal(new CanonicalOpening(48.5m, -110), Side(lines, Markets.Total, "under").Opening);
    }

    [Fact]
    public void Pickcenter_without_an_open_leaves_the_opening_null_and_the_close_as_it_was()
    {
        var without = Pickcenter
            .Replace(""", "open": { "odds": "-125" }""", "")
            .Replace(""", "open": { "line": "-1.5", "odds": "-110" }""", "")
            .Replace(""", "open": { "line": "o48.5", "odds": "-110" }""", "");

        var lines = FromPickcenter(without);
        var original = FromPickcenter(Pickcenter);

        Assert.Null(Side(lines, Markets.Moneyline, "Detroit Lions").Opening);
        Assert.Null(Side(lines, Markets.Spread, "Detroit Lions").Opening);
        Assert.Null(Side(lines, Markets.Total, "over").Opening);

        // Every close is the same with or without its open beside it.
        Assert.Equal(
            original.Select(l => (l.Market, l.Outcome, l.Line, l.PriceAmerican, l.Book)),
            lines.Select(l => (l.Market, l.Outcome, l.Line, l.PriceAmerican, l.Book)));
    }

    /// <summary>
    /// ESPN writes "OFF" for a price it took down — event 401910023's under, open and close alike.
    /// A close that is OFF yields no row, so its open has nothing to ride on; an open that is OFF
    /// beside a priced close keeps the line it opened at, without a price.
    /// </summary>
    [Fact]
    public void Pickcenter_open_taken_off_keeps_its_line_and_no_price()
    {
        var json = """
        {
          "pickcenter": [
            {
              "provider": { "name": "DraftKings" },
              "total": {
                "over":  { "close": { "line": "o228.5", "odds": "-113" }, "open": { "line": "o228.5", "odds": "OFF" } },
                "under": { "close": { "line": "u228.5", "odds": "OFF" }, "open": { "line": "u228.5", "odds": "OFF" } }
              }
            }
          ]
        }
        """;

        var line = Assert.Single(FromPickcenter(json));

        Assert.Equal("over", line.Outcome);
        Assert.Equal(new CanonicalOpening(228.5m, null), line.Opening);
    }

    [Fact]
    public void Core_odds_open_every_market_beside_its_close()
    {
        var lines = FromCore(CoreOdds);

        Assert.Equal(6, lines.Count);
        Assert.All(lines, l => Assert.Equal("DraftKings", l.Book));

        var homeMl = Side(lines, Markets.Moneyline, "Buffalo Bills");
        Assert.Equal(-1520, homeMl.PriceAmerican);
        Assert.Equal(new CanonicalOpening(null, -600), homeMl.Opening);
        Assert.Equal(new CanonicalOpening(null, 440), Side(lines, Markets.Moneyline, "Miami Dolphins").Opening);

        var awaySpread = Side(lines, Markets.Spread, "Miami Dolphins");
        Assert.Equal(16.5m, awaySpread.Line);
        Assert.Equal(new CanonicalOpening(11.5m, -110), awaySpread.Opening);
        Assert.Equal(new CanonicalOpening(-11.5m, -110), Side(lines, Markets.Spread, "Buffalo Bills").Opening);

        var over = Side(lines, Markets.Total, "over");
        Assert.Equal(44.5m, over.Line);
        Assert.Equal(new CanonicalOpening(47.5m, -110), over.Opening);
        Assert.Equal(new CanonicalOpening(47.5m, -110), Side(lines, Markets.Total, "under").Opening);
    }

    [Fact]
    public void Core_odds_without_an_open_leave_the_opening_null_and_the_close_as_it_was()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CoreOdds)!;
        var item = node["items"]![0]!.AsObject();
        item.Remove("open");
        item["homeTeamOdds"]!.AsObject().Remove("open");

        // The away side's open loses its spread price alone, and keeps the rest.
        item["awayTeamOdds"]!["open"]!.AsObject().Remove("spread");

        var lines = FromCore(node.ToJsonString());

        Assert.Null(Side(lines, Markets.Moneyline, "Buffalo Bills").Opening);
        Assert.Null(Side(lines, Markets.Spread, "Buffalo Bills").Opening);
        Assert.Null(Side(lines, Markets.Total, "over").Opening);
        Assert.Null(Side(lines, Markets.Total, "under").Opening);
        Assert.Equal(new CanonicalOpening(11.5m, null), Side(lines, Markets.Spread, "Miami Dolphins").Opening);
        Assert.Equal(new CanonicalOpening(null, 440), Side(lines, Markets.Moneyline, "Miami Dolphins").Opening);

        Assert.Equal(
            FromCore(CoreOdds).Select(l => (l.Market, l.Outcome, l.Line, l.PriceAmerican)),
            lines.Select(l => (l.Market, l.Outcome, l.Line, l.PriceAmerican)));
    }

    /// <summary>
    /// A pre-game odds document states the open and the current number but no close — 401873070's,
    /// read on 21 November 2026. An open is attached only to a close, so it yields nothing.
    /// </summary>
    [Fact]
    public void Core_odds_open_without_a_close_yields_nothing()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CoreOdds)!;
        var item = node["items"]![0]!.AsObject();
        item.Remove("close");
        item["homeTeamOdds"]!.AsObject().Remove("close");
        item["awayTeamOdds"]!.AsObject().Remove("close");

        Assert.Empty(FromCore(node.ToJsonString()));
    }

    /// <summary>
    /// A pick'em spread in the summary closes and opens at zero, as it does on the odds endpoint.
    /// The summary used to drop it, and with the moneyline read the odds endpoint was never asked,
    /// so the spread stayed missing. No pickcenter with a pick'em has been captured: the shape is
    /// the fixture's above with the lines written in words, as the odds endpoint writes them.
    /// </summary>
    [Fact]
    public void Pickcenter_pickem_spread_closes_and_opens_at_zero()
    {
        var json = Pickcenter
            .Replace("\"line\": \"-2.5\", \"odds\": \"-120\"", "\"line\": \"PK\", \"odds\": \"-110\"")
            .Replace("\"line\": \"+2.5\", \"odds\": \"+100\"", "\"line\": \"EVEN\", \"odds\": \"-110\"")
            .Replace("\"line\": \"-1.5\", \"odds\": \"-110\"", "\"line\": \" pk \", \"odds\": \"-110\"");

        var lines = FromPickcenter(json);

        Assert.Equal(6, lines.Count);

        var home = Side(lines, Markets.Spread, "Detroit Lions");
        Assert.Equal(0m, home.Line);
        Assert.Equal(-110, home.PriceAmerican);
        Assert.Equal(new CanonicalOpening(0m, -110), home.Opening);

        var away = Side(lines, Markets.Spread, "New England Patriots");
        Assert.Equal(0m, away.Line);
        Assert.Equal(new CanonicalOpening(1.5m, -110), away.Opening);
    }

    /// <summary>
    /// "EVEN" is a line of zero only for a spread. As a price it is +100, and a moneyline keeps no
    /// line whatever its block says; a total that says "EVEN" states no total and yields no row.
    /// </summary>
    [Fact]
    public void Pickcenter_even_is_a_price_of_100_and_never_a_total()
    {
        var json = """
        {
          "pickcenter": [
            {
              "provider": { "name": "DraftKings" },
              "moneyline": {
                "home": { "close": { "line": "EVEN", "odds": "EVEN" }, "open": { "line": "EVEN", "odds": "EVEN" } }
              },
              "total": {
                "over":  { "close": { "line": "EVEN", "odds": "-110" } },
                "under": { "close": { "line": "PK", "odds": "-110" } }
              }
            }
          ]
        }
        """;

        var line = Assert.Single(FromPickcenter(json));

        Assert.Equal(Markets.Moneyline, line.Market);
        Assert.Null(line.Line);
        Assert.Equal(100, line.PriceAmerican);
        Assert.Equal(new CanonicalOpening(null, 100), line.Opening);
    }

    /// <summary>
    /// On the odds endpoint a pick'em opens at zero as it closes, and a total written "PK" is no
    /// total: it used to be read as a total of zero.
    /// </summary>
    [Fact]
    public void Core_odds_pickem_opens_at_zero_and_a_worded_total_is_none()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CoreOdds)!;
        var item = node["items"]![0]!.AsObject();
        item["homeTeamOdds"]!["open"]!["pointSpread"]!["american"] = "PK";
        item["homeTeamOdds"]!["close"]!["pointSpread"]!["american"] = "EVEN";
        item["close"]!["total"]!["american"] = "PK";
        item["close"]!["total"]!["alternateDisplayValue"] = "PK";

        var lines = FromCore(node.ToJsonString());

        var home = Side(lines, Markets.Spread, "Buffalo Bills");
        Assert.Equal(0m, home.Line);
        Assert.Equal(new CanonicalOpening(0m, -110), home.Opening);
        Assert.DoesNotContain(lines, l => l.Market == Markets.Total);
    }
}
