using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// Deterministic offline odds source: prices the <i>real</i> schedule with plausible numbers
/// that drift between polls, so line-movement charts, CLV resolution and the whole reliability
/// loop can be exercised with no API key and no spend.
///
/// <para>
/// It used to invent its own fixtures too, from a fixed eight-team roster per sport. That left
/// the desk holding two disjoint sets of games — ESPN's real slate, unpriced, and invented
/// matchups with prices — so the board looked populated while being mostly fabricated, and a
/// genuine fixture like Yankees at Phillies showed a row of dashes because the fixture's teams
/// were not in the roster. Quoting the real slate is both more truthful and a better test: the
/// games now have to resolve by team and start time, which is exactly what a real provider's
/// events will have to do.
/// </para>
///
/// This is a development fixture, not a data provider: it is enabled by default precisely
/// so a cold clone of the repo runs end-to-end, and stands down once a real feed has a key.
/// </summary>
public class DemoOddsSource(
    IScheduleReader schedule,
    IOptions<IngestionOptions> options,
    ILogger<DemoOddsSource> logger) : IOddsSource, IFailureInjectable
{
    public const string SourceKey = "demo";

    /// <summary>
    /// How far ahead to quote. Matches the board's widest window, so anything an operator can
    /// look at has been offered a price.
    /// </summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(48);

    public string Key => SourceKey;

    public IReadOnlyList<string> SupportedMarkets => Markets.V1;

    /// <summary>Applied from the source row before each run, so a drill survives a restart.</summary>
    public string? FailureMode { get; set; }

    /// <summary>
    /// The books to fabricate, taken from configuration rather than hard-coded.
    ///
    /// Two books was enough while the demo only had to make a movement chart wiggle. The board
    /// asks a different question — <i>which</i> book has the best number — and that question is
    /// meaningless with two and pointless with one. A fixture that cannot exercise the screen
    /// it exists to demonstrate is not doing its job, so it now shadows whatever real book list
    /// is configured.
    /// </summary>
    private string[] Books => options.Value.OddsApiIo.EffectiveBookmakers;

    /// <summary>
    /// Each book leans a fixed amount, derived from its name so a book always leans the same
    /// way. Without a per-book bias every book quotes the same number, and the board's whole
    /// point — that they disagree — never appears.
    ///
    /// <para>
    /// The hash is computed here rather than taken from <see cref="string.GetHashCode()"/>,
    /// which is seeded per process: the same book leaned a different way on every restart, and
    /// nothing guaranteed four books did not all land on the same lean in a given run — which is
    /// exactly the state in which this fixture demonstrates nothing. "Derived from its name" has
    /// to mean stable across processes or it is not derived from the name at all.
    /// </para>
    /// </summary>
    private static int BiasFor(string book) => (StableHash(book.ToLowerInvariant()) % 17) - 8;

    /// <summary>
    /// FNV-1a. Small, and — unlike the framework's hashing — the same on every process, which is
    /// the only property being asked of it here.
    /// </summary>
    private static int StableHash(string value)
    {
        uint hash = 2166136261;

        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

    public async Task<OddsFetchResult> FetchSlateAsync(
        string sportKey,
        IReadOnlyList<string> markets,
        CancellationToken ct)
    {
        if (ApplyFailureMode())
            return new OddsFetchResult([], [], new FetchCost(1));

        var now = DateTimeOffset.UtcNow;
        var games = new List<CanonicalGame>();
        var odds = new List<CanonicalOdds>();

        // Price the real schedule.
        //
        // This used to invent its own fixtures from a fixed eight-team roster, which left the
        // desk holding two disjoint sets of games: ESPN's real slate with no prices, and
        // invented matchups with prices for teams that were not playing. A board of mostly
        // fabricated rows looks populated, which is worse than looking empty — and the one real
        // game on it read as a bug in the odds feed rather than a fixture nobody had quoted.
        //
        // Quoting the real slate also puts this fixture through the same entity resolution a
        // real provider will use: it publishes team names and a start time and nothing else, so
        // the games have to match on that.
        var scheduled = await schedule.GetUpcomingAsync(sportKey, Horizon, ct);

        if (scheduled.Count == 0)
        {
            logger.LogInformation(
                "{Source}: no {Sport} fixtures to price — ingest the schedule first", SourceKey, sportKey);

            return new OddsFetchResult([], [], new FetchCost(1));
        }

        foreach (var fixture in scheduled)
        {
            var home = fixture.HomeTeamName;
            var away = fixture.AwayTeamName;
            var startsAt = fixture.StartsAt;

            // Stable per fixture, so a re-run quotes the same game rather than a new one.
            var sourceGameId = $"demo-{sportKey}-{home}-{away}-{startsAt:yyyyMMddHHmm}"
                .Replace(' ', '-')
                .ToLowerInvariant();

            games.Add(new CanonicalGame(sourceGameId, sportKey, home, away, startsAt, "scheduled"));

            // Seed per game so prices are stable across a run but drift as time passes —
            // that drift is what makes movement charts and CLV meaningful.
            // Stable across restarts, not merely within one. HashCode.Combine is seeded per
            // process, so the same fixture at the same hour priced differently after every
            // restart — which makes a movement chart record the deployment rather than a market.
            var seed = StableHash($"{sourceGameId}|{now.Hour / 3}");
            var rng = new Random(seed);
            var edge = rng.Next(-140, 141);

            foreach (var book in Books)
            {
                var bookBias = BiasFor(book);

                // Books also disagree on the number itself, not only the price — a half point
                // moves between them, which is the case the board's comparator has to rank
                // line-first rather than price-first.
                var lineShift = (bookBias % 3) * 0.5m;

                if (markets.Contains(Markets.Moneyline))
                {
                    odds.Add(Price(sourceGameId, book, Markets.Moneyline, home, null,
                        Balance(-110 + edge + bookBias), now));
                    odds.Add(Price(sourceGameId, book, Markets.Moneyline, away, null,
                        Balance(-110 - edge - bookBias), now));
                }

                if (markets.Contains(Markets.Spread))
                {
                    var spread = Math.Round(edge / 45.0m * 2, MidpointRounding.AwayFromZero) / 2 + lineShift;
                    odds.Add(Price(sourceGameId, book, Markets.Spread, home, -spread, -110 + bookBias, now));
                    odds.Add(Price(sourceGameId, book, Markets.Spread, away, spread, -110 - bookBias, now));
                }

                if (markets.Contains(Markets.Total))
                {
                    var total = sportKey switch
                    {
                        "nfl" => 44.5m,
                        "nba" => 224.5m,
                        "mlb" => 8.5m,
                        _ => 6.5m
                    } + rng.Next(-4, 5) * 0.5m + lineShift;

                    odds.Add(Price(sourceGameId, book, Markets.Total, "over", total, -110 + bookBias, now));
                    odds.Add(Price(sourceGameId, book, Markets.Total, "under", total, -110 - bookBias, now));
                }
            }
        }

        logger.LogInformation("{Source}: priced {Games} {Sport} fixtures, {Odds} prices",
            SourceKey, games.Count, sportKey, odds.Count);

        return new OddsFetchResult(games, odds, new FetchCost(1));
    }

    private static CanonicalOdds Price(
        string gameId, string book, string market, string outcome,
        decimal? line, int price, DateTimeOffset at)
        => new(gameId, book, market, outcome, line, Balance(price), at);

    /// <summary>American odds have no values between -100 and +100; snap into the legal range.</summary>
    /// <summary>
    /// Moves a price out of the gap American odds do not use, without flattening the ones that
    /// land in it.
    ///
    /// <para>
    /// There is no such price as -50 or +40: the scale runs ...-102, -101, -100, +100, +101...
    /// so -100 and +100 are neighbours meaning the same thing. Clamping to the nearer edge — the
    /// obvious reading, and what this did — maps every value in the gap onto exactly two numbers.
    /// Four books quoting four different near-even prices came out identical, so the fixture
    /// showed perfect agreement precisely where it meant to show a spread, and did it more often
    /// the closer a game was to a coin flip.
    /// </para>
    ///
    /// <para>
    /// Adding 200 steps across the gap instead, which is continuous and order-preserving: -101
    /// stays -101, -99 becomes +101, -95 becomes +105. Books that disagreed still disagree, and
    /// by the same amount.
    /// </para>
    /// </summary>
    private static int Balance(int price)
        => price is > -100 and < 100 ? price + 200 : price;

    /// <summary>Returns true when the injected mode should yield a silent empty payload.</summary>
    private bool ApplyFailureMode()
    {
        switch (FailureMode)
        {
            case "error":
                throw new HttpRequestException("Injected failure: simulated upstream 503.");
            case "timeout":
                throw new TaskCanceledException("Injected failure: simulated request timeout.");
            case "empty":
                // The nastiest real-world case: HTTP 200 with no rows. This must surface as a
                // volume anomaly rather than an error — that contrast is the point of the drill.
                logger.LogWarning("{Source}: injected empty-payload failure", SourceKey);
                return true;
            default:
                return false;
        }
    }
}
