using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;

namespace LineOps.Web.Tests.Support;

/// <summary>
/// A board row to render the wager window and the slip against. The standard row is an NFL game,
/// Bills at Chiefs, whose most valuable side is the Chiefs' moneyline at DraftKings, -128, worth
/// +2.7% against a Pinnacle fair price of -136; FanDuel also quotes it at -135, captured 40 minutes ago.
/// </summary>
public static class BoardRows
{
    public const int GameId = 7;
    public const string Home = "Kansas City Chiefs";
    public const string Away = "Buffalo Bills";

    public static readonly FairPrice FairHomeMl = new(0.576, -136, FairValue.SharpBook, 1);

    public static Game Game(DateTimeOffset? startsAt = null) => new()
    {
        Id = GameId,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        HomeTeam = new Team { Name = Home, Abbrev = "KC" },
        AwayTeam = new Team { Name = Away, Abbrev = "BUF" },
        StartsAt = startsAt ?? DateTimeOffset.UtcNow.AddHours(3),
        Status = GameStatus.Scheduled
    };

    public static BookPrice Rung(string book, int american, decimal? line = null, double? ev = null, DateTimeOffset? capturedAt = null)
        => new(book, american, line, OddsMath.ImpliedProbability(american)) { Ev = ev, CapturedAt = capturedAt };

    public static BestOffer Offer(string outcome, string book, int american, decimal? line = null,
        double? ev = null, FairPrice? fair = null, bool closing = false, TimeSpan? age = null, BookPrice[]? rungs = null)
    {
        var captured = DateTimeOffset.UtcNow - (age ?? TimeSpan.FromMinutes(12));
        return new BestOffer(outcome, book, american, line, captured, null, false, closing,
            rungs ?? [Rung(book, american, line, ev, captured)])
        {
            Fair = fair,
            Ev = ev
        };
    }

    public static BoardRow Row(int homeMl = -128, TimeSpan? age = null, bool closing = false, DateTimeOffset? startsAt = null)
    {
        var ageOf = age ?? TimeSpan.FromMinutes(12);

        var homeMlOffer = Offer(Home, "draftkings", homeMl, ev: 0.027, fair: FairHomeMl, closing: closing, age: ageOf,
            rungs:
            [
                Rung("draftkings", homeMl, ev: 0.027, capturedAt: DateTimeOffset.UtcNow - ageOf),
                Rung("fanduel", -135, ev: -0.01, capturedAt: DateTimeOffset.UtcNow.AddMinutes(-40))
            ]);

        return new BoardRow(
            Game(startsAt),
            new MarketPair(Markets.Moneyline, homeMlOffer,
                Offer(Away, "fanduel", 110, ev: -0.02, closing: closing, age: ageOf)),
            new MarketPair(Markets.Spread,
                Offer(Home, "draftkings", -110, line: -2.5m, closing: closing, age: ageOf),
                Offer(Away, "fanduel", -105, line: 2.5m, ev: 0.01, closing: closing, age: ageOf)),
            new MarketPair(Markets.Total,
                Offer("Over", "draftkings", -110, line: 47.5m, closing: closing, age: ageOf),
                Offer("Under", "fanduel", -110, line: 47.5m, closing: closing, age: ageOf)),
            BookCount: 2,
            PricedAt: DateTimeOffset.UtcNow - ageOf,
            PricesAreClosing: closing,
            Unpriced: null);
    }
}
