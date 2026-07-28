using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.CrossReference;

/// <summary>
/// The board: today's slate with the best number available on each market, and which book has it.
///
/// <para>
/// This is the screen the product exists for. Four books disagree, the disagreement is money,
/// and the job is to show where it is without making anyone read four price tables. So each
/// market shows one number — the best one — the book holding it, and how far the rest of the
/// market sits behind. A market where the books agree should look flat; one worth shopping
/// should look wide.
/// </para>
///
/// <para>
/// Read live from the scan tier. Nothing here is precomputed, because "best price" is only
/// true until the next scan and a cached board is a board that lies quietly.
/// </para>
/// </summary>
public class BoardService(LineOpsDbContext db)
{
    /// <summary>
    /// Games starting inside the window, with the best available price on each market.
    /// </summary>
    public async Task<IReadOnlyList<BoardRow>> GetAsync(
        string? sportKey,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now + window;

        var games = await db.Games
            .Include(g => g.Sport)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.StartsAt >= now.AddHours(-3) && g.StartsAt <= horizon)
            .Where(g => sportKey == null || g.Sport!.Key == sportKey)
            .OrderBy(g => g.StartsAt)
            .AsNoTracking()
            .ToListAsync(ct);

        if (games.Count == 0)
            return [];

        var gameIds = games.Select(g => g.Id).ToList();

        // Newest observation per book, market and outcome. The scan tier is append-only, so
        // "current" is the latest row rather than a column.
        var latest = await db.OddsSnapshots
            .Where(s => gameIds.Contains(s.GameId))
            .GroupBy(s => new { s.GameId, s.Book, s.Market, s.Outcome })
            .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
            .AsNoTracking()
            .ToListAsync(ct);

        var byGame = latest.GroupBy(s => s.GameId).ToDictionary(g => g.Key, g => g.ToList());

        var newest = latest.Count == 0 ? (DateTimeOffset?)null : latest.Max(s => s.CapturedAt);

        return games.Select(game =>
        {
            var prices = byGame.GetValueOrDefault(game.Id, []);

            return new BoardRow(
                Game: game,
                Moneyline: BestPair(prices, Markets.Moneyline, game),
                Spread: BestPair(prices, Markets.Spread, game),
                Total: BestPair(prices, Markets.Total, game),
                BookCount: prices.Select(p => p.Book).Distinct().Count(),
                PricedAt: prices.Count == 0 ? null : prices.Max(p => p.CapturedAt),
                Unpriced: prices.Count > 0 ? null : ExplainGap(game, newest));
        }).ToList();
    }

    /// <summary>
    /// Why a game has no price, in the operator's terms.
    ///
    /// A row of dashes is ambiguous between three very different situations — the odds feed is
    /// broken, this fixture was never offered, or the game has already started — and only one
    /// of them is worth acting on. Saying which turns a silent gap into a fact.
    /// </summary>
    private static string ExplainGap(Game game, DateTimeOffset? newestScanAnywhere)
    {
        if (game.StartsAt <= DateTimeOffset.UtcNow)
            return "Under way — pre-match prices are dropped once a game starts.";

        return newestScanAnywhere is null
            ? "No odds scan has run yet. Pull data → Fresh lines."
            : "The last scan did not cover this fixture.";
    }

    /// <summary>
    /// One game's row, for a view opened against a game id rather than handed a row.
    ///
    /// The board's actions can open either as a desk window or as a floating panel, and a
    /// window is reconstructed from its parameters on every render — so the views take an id
    /// and load, rather than closing over a row that a window cannot carry.
    /// </summary>
    public async Task<BoardRow?> GetRowAsync(int gameId, CancellationToken ct = default)
    {
        var game = await db.Games
            .Include(g => g.Sport)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);

        if (game is null)
            return null;

        var prices = await db.OddsSnapshots
            .Where(s => s.GameId == gameId)
            .GroupBy(s => new { s.Book, s.Market, s.Outcome })
            .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
            .AsNoTracking()
            .ToListAsync(ct);

        return new BoardRow(
            Game: game,
            Moneyline: BestPair(prices, Markets.Moneyline, game),
            Spread: BestPair(prices, Markets.Spread, game),
            Total: BestPair(prices, Markets.Total, game),
            BookCount: prices.Select(p => p.Book).Distinct().Count(),
            PricedAt: prices.Count == 0 ? null : prices.Max(p => p.CapturedAt),
            Unpriced: prices.Count > 0 ? null : ExplainGap(game, null));
    }

    /// <summary>Both sides of one market, each with its own best book.</summary>
    private static MarketPair BestPair(List<OddsSnapshot> prices, string market, Game game)
    {
        var inMarket = prices.Where(p => p.Market == market).ToList();

        if (inMarket.Count == 0)
            return new MarketPair(market, null, null);

        var outcomes = inMarket.Select(p => p.Outcome).Distinct().ToList();

        // Totals name their sides; team markets use the team names, and home leads.
        var (first, second) = market == Markets.Total
            ? ("over", "under")
            : (game.HomeTeam?.Name ?? outcomes.FirstOrDefault() ?? "",
               game.AwayTeam?.Name ?? outcomes.Skip(1).FirstOrDefault() ?? "");

        return new MarketPair(
            market,
            Best(inMarket.Where(p => p.Outcome == first).ToList(), market),
            Best(inMarket.Where(p => p.Outcome == second).ToList(), market));
    }

    /// <summary>
    /// The best offer for one side, and where the rest of the market sits.
    ///
    /// <para>
    /// "Best" is not simply the biggest number. On a moneyline it is the lowest implied
    /// probability — the most you get paid for the same bet. On a handicap the <i>line</i>
    /// outranks the price, because half a point is usually worth more than a few cents:
    /// taking +1.5 at -110 beats +1.0 at -105, and a comparator that only read prices would
    /// recommend the worse bet with total confidence.
    /// </para>
    ///
    /// <para>
    /// Which line is better depends on the side. An over wants the lowest total and an under
    /// the highest; a handicap wants the most points it can get. That asymmetry is why this
    /// is a per-market comparator rather than one <c>OrderByDescending</c>.
    /// </para>
    /// </summary>
    private static BestOffer? Best(List<OddsSnapshot> side, string market)
    {
        if (side.Count == 0)
            return null;

        var ranked = market switch
        {
            Markets.Moneyline => side
                .OrderBy(p => OddsMath.ImpliedProbability(p.PriceAmerican))
                .ToList(),

            Markets.Total when side[0].Outcome.Equals("under", StringComparison.OrdinalIgnoreCase) =>
                side.OrderByDescending(p => p.Line ?? 0)
                    .ThenBy(p => OddsMath.ImpliedProbability(p.PriceAmerican))
                    .ToList(),

            Markets.Total => side
                .OrderBy(p => p.Line ?? 0)
                .ThenBy(p => OddsMath.ImpliedProbability(p.PriceAmerican))
                .ToList(),

            // Spread and anything new: more points is better for the side holding them.
            _ => side
                .OrderByDescending(p => p.Line ?? 0)
                .ThenBy(p => OddsMath.ImpliedProbability(p.PriceAmerican))
                .ToList()
        };

        var best = ranked[0];

        // Every book's standing, best first — the spread rail is drawn from this.
        var rungs = ranked
            .Select(p => new BookPrice(
                p.Book,
                p.PriceAmerican,
                p.Line,
                OddsMath.ImpliedProbability(p.PriceAmerican)))
            .ToList();

        // What shopping is worth here, in points of implied probability against the worst book.
        double? edge = rungs.Count > 1
            ? (rungs[^1].Implied - rungs[0].Implied) * 100
            : null;

        // Whether the books are even offering the same bet.
        //
        // This matters more than the price gap and is not visible in it: a market where every
        // book quotes -110 but on totals of 7, 8 and 9 has an identical price spread of zero
        // and a large real difference. Reporting that as "books agree" would be confidently
        // wrong about the one case worth shopping hardest.
        var linesVary = rungs.Select(r => r.Line).Distinct().Count() > 1;

        return new BestOffer(
            Outcome: best.Outcome,
            Book: best.Book,
            PriceAmerican: best.PriceAmerican,
            Line: best.Line,
            CapturedAt: best.CapturedAt,
            EdgePoints: edge,
            LinesVary: linesVary,
            Rungs: rungs);
    }
}

/// <summary>One game on the board.</summary>
public record BoardRow(
    Game Game,
    MarketPair Moneyline,
    MarketPair Spread,
    MarketPair Total,
    int BookCount,
    DateTimeOffset? PricedAt,
    string? Unpriced)
{
    public bool HasPrices => BookCount > 0;

    /// <summary>How old the newest price on this row is.</summary>
    public TimeSpan? Age => PricedAt is { } at ? DateTimeOffset.UtcNow - at : null;
}

/// <summary>Both sides of a market. Home/over first, away/under second.</summary>
public record MarketPair(string Market, BestOffer? First, BestOffer? Second)
{
    public bool IsPriced => First is not null || Second is not null;
}

/// <summary>The best offer on one side, plus where every other book stands.</summary>
public record BestOffer(
    string Outcome,
    string Book,
    int PriceAmerican,
    decimal? Line,
    DateTimeOffset CapturedAt,
    double? EdgePoints,
    bool LinesVary,
    IReadOnlyList<BookPrice> Rungs);

/// <summary>One book's standing on a side.</summary>
public record BookPrice(string Book, int PriceAmerican, decimal? Line, double Implied);
