using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// The line as it closed, read from the block ESPN attaches to a finished game.
///
/// <para>
/// The payload here is the real shape returned for event 401816587 (Pirates–Tigers,
/// 19 August 2026), trimmed to the fields that are read. ESPN states <c>close</c> alongside
/// <c>open</c>, so these are the provider's own closing numbers rather than the last value a
/// scan happened to catch.
/// </para>
/// </summary>
public class EspnClosingLineTests
{
    private const string Payload = """
    {
      "pickcenter": [
        {
          "provider": { "id": "100", "name": "DraftKings" },
          "overUnder": 8.5,
          "spread": -1.5,
          "pointSpread": {
            "home": { "close": { "line": "-1.5", "odds": "+130" }, "open": { "line": "-1.5", "odds": "+149" } },
            "away": { "close": { "line": "+1.5", "odds": "-157" }, "open": { "line": "+1.5", "odds": "-181" } }
          },
          "total": {
            "over":  { "close": { "line": "o8.5", "odds": "-107" }, "open": { "line": "o8", "odds": "-112" } },
            "under": { "close": { "line": "u8.5", "odds": "-112" }, "open": { "line": "u8", "odds": "-108" } }
          },
          "moneyline": {
            "home": { "close": { "odds": "-154" }, "open": { "odds": "-139" } },
            "away": { "close": { "odds": "+143" }, "open": { "odds": "+116" } }
          }
        }
      ]
    }
    """;

    private static readonly DateTimeOffset Close =
        new(2026, 8, 19, 16, 35, 0, TimeSpan.Zero);

    private static List<Core.Contracts.CanonicalOdds> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        return EspnStatsAdapter.ParseClosingLines(
            doc.RootElement, "401816587", "Pittsburgh Pirates", "Detroit Tigers", Close).ToList();
    }

    [Fact]
    public void Every_market_is_read_from_both_sides()
    {
        var lines = Parse(Payload);

        Assert.Equal(6, lines.Count);
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Moneyline));
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Spread));
        Assert.Equal(2, lines.Count(l => l.Market == Markets.Total));
        Assert.All(lines, l => Assert.Equal("DraftKings", l.Book));
        Assert.All(lines, l => Assert.Equal(Close, l.CapturedAt));
    }

    /// <summary>
    /// Sides are named as the platform names them, so grading and display can find them without
    /// knowing which provider they came from.
    /// </summary>
    [Fact]
    public void Sides_are_named_by_team_and_by_direction()
    {
        var lines = Parse(Payload);

        var ml = lines.Where(l => l.Market == Markets.Moneyline).ToList();
        Assert.Contains(ml, l => l.Outcome == "Pittsburgh Pirates" && l.PriceAmerican == -154);
        Assert.Contains(ml, l => l.Outcome == "Detroit Tigers" && l.PriceAmerican == 143);

        var total = lines.Where(l => l.Market == Markets.Total).ToList();
        Assert.Contains(total, l => l.Outcome == "over");
        Assert.Contains(total, l => l.Outcome == "under");
    }

    /// <summary>
    /// The close, not the open. Reading the wrong one would look entirely plausible and be a
    /// different number — here -1.5/+130 rather than -1.5/+149.
    /// </summary>
    [Fact]
    public void The_closing_price_is_taken_rather_than_the_opening_one()
    {
        var home = Parse(Payload).Single(l => l.Market == Markets.Spread && l.Outcome == "Pittsburgh Pirates");

        Assert.Equal(130, home.PriceAmerican);
        Assert.Equal(-1.5m, home.Line);
    }

    /// <summary>
    /// Totals arrive decorated with the side they belong to — "o8.5" — and a line stored as
    /// text or dropped would make the market ungradeable.
    /// </summary>
    [Fact]
    public void Total_lines_are_stripped_of_their_over_under_marker()
    {
        var lines = Parse(Payload).Where(l => l.Market == Markets.Total).ToList();

        Assert.All(lines, l => Assert.Equal(8.5m, l.Line));
        Assert.Equal(-107, lines.Single(l => l.Outcome == "over").PriceAmerican);
        Assert.Equal(-112, lines.Single(l => l.Outcome == "under").PriceAmerican);
    }

    [Fact]
    public void A_game_with_no_odds_block_yields_nothing()
    {
        Assert.Empty(Parse("""{ "boxscore": {} }"""));
        Assert.Empty(Parse("""{ "pickcenter": [] }"""));
    }

    /// <summary>
    /// A market the provider did not price is skipped rather than stored at zero — the same
    /// rule the rest of the platform follows about absent versus zero (ADR 0003).
    /// </summary>
    [Fact]
    public void A_market_without_a_close_is_skipped_rather_than_defaulted()
    {
        var partial = """
        {
          "pickcenter": [{
            "provider": { "name": "DraftKings" },
            "moneyline": { "home": { "close": { "odds": "-154" } }, "away": { "open": { "odds": "+143" } } }
          }]
        }
        """;

        var line = Assert.Single(Parse(partial));

        Assert.Equal(Markets.Moneyline, line.Market);
        Assert.Equal("Pittsburgh Pirates", line.Outcome);
    }

    [Theory]
    [InlineData("+130", 130)]
    [InlineData("-157", -157)]
    [InlineData("EVEN", 100)]
    [InlineData(" -110 ", -110)]
    public void American_prices_are_parsed_as_written(string raw, int expected)
    {
        Assert.True(EspnStatsAdapter.TryParseAmerican(raw, out var price));
        Assert.Equal(expected, price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    [InlineData(null)]
    public void An_unreadable_price_is_refused_rather_than_guessed(string? raw)
    {
        Assert.False(EspnStatsAdapter.TryParseAmerican(raw, out _));
    }
}
