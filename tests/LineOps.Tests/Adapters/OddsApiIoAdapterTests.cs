using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// Parsing is pinned against recorded provider payloads rather than a live call, so the
/// suite stays deterministic, offline and free. The fixtures deliberately include the
/// awkward shapes seen in real responses: nested team objects, prices as strings, a market
/// we do not model, and a malformed row.
/// </summary>
public class OddsApiIoAdapterTests
{
    private static JsonElement Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void ParseEvents_UnwrapsDataEnvelopeAndReadsBothTeamShapes()
    {
        var events = OddsApiIoAdapter.ParseEvents(Load("odds-api-io.events.json"), "nfl").ToList();

        // Three rows in the fixture, but one has no home team and must be dropped.
        Assert.Equal(2, events.Count);

        var first = events[0];
        Assert.Equal("evt-1001", first.SourceGameId);
        Assert.Equal("Seattle Seahawks", first.HomeTeamName);
        Assert.Equal("San Francisco 49ers", first.AwayTeamName);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 20, 5, 0, TimeSpan.Zero), first.StartsAt);

        // Nested { name: "..." } objects resolve the same as plain strings.
        Assert.Equal("Green Bay Packers", events[1].HomeTeamName);
        Assert.Equal("Dallas Cowboys", events[1].AwayTeamName);
    }

    [Fact]
    public void ParseEvents_SkipsRowsMissingRequiredFields()
    {
        var events = OddsApiIoAdapter.ParseEvents(Load("odds-api-io.events.json"), "nfl").ToList();

        Assert.DoesNotContain(events, e => e.SourceGameId == "evt-1003");
    }

    [Fact]
    public void ParseOdds_NormalisesProviderMarketNames()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var odds = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), Markets.V1, capturedAt)
            .ToList();

        Assert.Contains(odds, o => o.Market == Markets.Moneyline);
        Assert.Contains(odds, o => o.Market == Markets.Spread);
        Assert.Contains(odds, o => o.Market == Markets.Total);

        // "asian handicap corners" is not a market we model; it must not leak through.
        Assert.All(odds, o => Assert.Contains(o.Market, Markets.V1));
    }

    [Fact]
    public void ParseOdds_ReadsLinesFromEitherPointOrTotal()
    {
        var odds = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), Markets.V1, DateTimeOffset.UtcNow)
            .ToList();

        var spread = odds.First(o => o.Market == Markets.Spread && o.Outcome == "Seattle Seahawks");
        Assert.Equal(-2.5m, spread.Line);

        var total = odds.First(o => o.Market == Markets.Total && o.Outcome == "over");
        Assert.Equal(44.5m, total.Line);
    }

    [Fact]
    public void ParseOdds_AcceptsNumericPricesEncodedAsStrings()
    {
        var odds = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), Markets.V1, DateTimeOffset.UtcNow)
            .ToList();

        var fanduel = odds.First(o => o.Book == "fanduel" && o.Outcome == "Seattle Seahawks");
        Assert.Equal(-150, fanduel.PriceAmerican);
    }

    [Fact]
    public void ParseOdds_DropsRowsWithAnUnusablePrice()
    {
        var odds = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), Markets.V1, DateTimeOffset.UtcNow)
            .ToList();

        // Zero is not a valid American price and would break every downstream conversion.
        Assert.DoesNotContain(odds, o => o.Outcome == "Bad Row");
        Assert.All(odds, o => Assert.NotEqual(0, o.PriceAmerican));
    }

    [Fact]
    public void ParseOdds_LowercasesBookKeysForStableGrouping()
    {
        var odds = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), Markets.V1, DateTimeOffset.UtcNow)
            .ToList();

        Assert.Contains(odds, o => o.Book == "draftkings");
        Assert.Contains(odds, o => o.Book == "fanduel");
        Assert.DoesNotContain(odds, o => o.Book != o.Book.ToLowerInvariant());
    }

    [Fact]
    public void ParseOdds_HonoursTheRequestedMarketFilter()
    {
        var onlyMoneyline = OddsApiIoAdapter
            .ParseOdds(Load("odds-api-io.odds.json"), [Markets.Moneyline], DateTimeOffset.UtcNow)
            .ToList();

        Assert.NotEmpty(onlyMoneyline);
        Assert.All(onlyMoneyline, o => Assert.Equal(Markets.Moneyline, o.Market));
    }

    [Fact]
    public void ParseOdds_ReturnsNothingForAnUnexpectedShape()
    {
        // Schema drift must degrade to zero rows — which surfaces as a volume anomaly —
        // rather than throwing and taking the run down.
        var garbage = JsonDocument.Parse("""{"unexpected":"shape"}""").RootElement;

        var odds = OddsApiIoAdapter.ParseOdds(garbage, Markets.V1, DateTimeOffset.UtcNow).ToList();
        var events = OddsApiIoAdapter.ParseEvents(garbage, "nfl").ToList();

        Assert.Empty(odds);
        Assert.Empty(events);
    }
}
