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
    /// Games inside the window, with the best available price on each market.
    /// </summary>
    /// <param name="sportKey">One league, or null for every one in play.</param>
    /// <param name="window">How far ahead to look.</param>
    /// <param name="lookback">
    /// How far <i>back</i> to look. Games already under way or finished belong on the board:
    /// they carry a score and a closing line, and a surface that drops them at first pitch is
    /// one an operator has to leave in order to see how the day went.
    /// </param>
    public async Task<IReadOnlyList<BoardRow>> GetAsync(
        string? sportKey,
        TimeSpan window,
        TimeSpan? lookback = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now + window;
        var floor = now - (lookback ?? DefaultLookback);

        var games = await db.Games
            .Include(g => g.Sport)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.StartsAt >= floor && g.StartsAt <= horizon)
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

        var byGame = latest
            .GroupBy(s => s.GameId)
            .ToDictionary(g => g.Key, g => g.Select(Quote.From).ToList());

        // Anything the scan tier no longer holds is asked of the permanent record. That is the
        // whole point of promotion (ADR 0010): the close outlives the stream, so a game in play
        // still has a number even though its scans are gone.
        var missing = gameIds.Where(id => !byGame.ContainsKey(id)).ToList();

        var closesByGame = missing.Count == 0
            ? []
            : MarketFirst(await db.ClosingLines
                .Where(c => missing.Contains(c.GameId))
                .Select(c => new ClosedQuote(c, c.Source!.Kind))
                .AsNoTracking()
                .ToListAsync(ct));

        // Only asked for the leagues with a row that will have to explain itself. It used to be
        // the newest scan among the window's own games, so a slate nobody had priced yet — the
        // morning after a pull, or any day under manual polling — told every row that no scan
        // had ever run, on a desk with weeks of scans behind it.
        var unexplained = games
            .Where(g => !byGame.ContainsKey(g.Id) && !closesByGame.ContainsKey(g.Id))
            .Select(g => g.SportId)
            .Distinct()
            .ToList();

        var lastPull = unexplained.Count == 0
            ? []
            : await LastPullBySportAsync(
                games.Where(g => unexplained.Contains(g.SportId)).Select(g => g.Sport!).DistinctBy(s => s.Id).ToList(),
                ct);

        return games.Select(game => Compose(
            game,
            byGame.GetValueOrDefault(game.Id),
            closesByGame.GetValueOrDefault(game.Id),
            lastPull.TryGetValue(game.SportId, out var at) ? at : null)).ToList();
    }

    /// <summary>
    /// When each league was last priced: its newest scan, or its last successful lines pull,
    /// whichever is later.
    ///
    /// <para>
    /// The scans alone cannot answer it. Promotion deletes a game's scans at first pitch (ADR
    /// 0010), so once every game a manual pull priced has started, the league has no scans left
    /// and looked as though it had never been priced at all. The run log keeps the pull. Only a
    /// league's own pull counts (<c>odds:lines:mlb</c>): the all-leagues job does not record
    /// which leagues it reached, and it runs under automatic polling, which keeps the upcoming
    /// slate in the scan tier anyway.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, DateTimeOffset>> LastPullBySportAsync(List<Sport> sports, CancellationToken ct)
    {
        var ids = sports.Select(s => s.Id).ToList();

        var scans = await db.OddsSnapshots
            .Where(s => ids.Contains(s.Game!.SportId))
            .GroupBy(s => s.Game!.SportId)
            .Select(g => new { SportId = g.Key, At = g.Max(s => s.CapturedAt) })
            .ToDictionaryAsync(x => x.SportId, x => x.At, ct);

        var jobs = sports.ToDictionary(s => OddsLinesJobPrefix + s.Key, s => s.Id);
        var jobKeys = jobs.Keys.ToList();

        var pulls = await db.IngestionRuns
            .Where(r => jobKeys.Contains(r.JobKey) && (r.Status == RunStatus.Success || r.Status == RunStatus.Partial))
            .GroupBy(r => r.JobKey)
            .Select(g => new { JobKey = g.Key, At = g.Max(r => r.StartedAt) })
            .ToListAsync(ct);

        foreach (var pull in pulls)
        {
            var sportId = jobs[pull.JobKey];
            if (!scans.TryGetValue(sportId, out var scanned) || pull.At > scanned)
                scans[sportId] = pull.At;
        }

        return scans;
    }

    /// <summary>
    /// The job key of a one-league lines pull, less the league. Mirrors
    /// <c>IngestionJobs.OddsLinesPrefix</c>, which this project cannot reference.
    /// </summary>
    public const string OddsLinesJobPrefix = "odds:lines:";

    /// <summary>A closing line with the kind of source that wrote it.</summary>
    private sealed record ClosedQuote(ClosingLine Line, SourceKind Kind);

    /// <summary>
    /// The close per game, read from the book market where there is one.
    ///
    /// The stats provider records a single-book reference close for every final under its own
    /// source. It is never mixed into a market: a game the odds feed covered is read from the
    /// market alone, and only a game it never saw falls back to the reference — otherwise the
    /// reference would count as one more book, could win "best", and would move the number it
    /// was meant to be compared against (ADR 0011). Same rule as the game log's reader.
    /// </summary>
    private static Dictionary<int, List<Quote>> MarketFirst(List<ClosedQuote> closed)
        => closed
            .GroupBy(c => c.Line.GameId)
            .ToDictionary(
                g => g.Key,
                g => (g.Any(c => c.Kind == SourceKind.Odds) ? g.Where(c => c.Kind == SourceKind.Odds) : g)
                    .Select(c => Quote.From(c.Line))
                    .ToList());

    /// <summary>
    /// Three hours back by default, which is roughly a game.
    ///
    /// The board used to stop here and it was the wrong ceiling for a surface that also has to
    /// answer "how did tonight go" — but it stays the default so a caller that wants only the
    /// live market gets it without saying so.
    /// </summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(3);

    /// <summary>
    /// One row from whichever tier has numbers for it, live market preferred.
    ///
    /// A game with scans is still being priced, so the scans are the truth. A game without them
    /// has either started — in which case the close is the truth — or was never covered, in
    /// which case the row says so rather than showing a dash and leaving it to be guessed.
    /// </summary>
    private static BoardRow Compose(
        Game game,
        List<Quote>? live,
        List<Quote>? closing,
        DateTimeOffset? lastScanInLeague)
    {
        var isClosing = live is not { Count: > 0 } && closing is { Count: > 0 };
        var prices = live is { Count: > 0 } ? live : closing ?? [];

        return new BoardRow(
            Game: game,
            Moneyline: BestPair(prices, Markets.Moneyline, game, isClosing),
            Spread: BestPair(prices, Markets.Spread, game, isClosing),
            Total: BestPair(prices, Markets.Total, game, isClosing),
            BookCount: prices.Select(p => p.Book).Distinct().Count(),
            PricedAt: prices.Count == 0 ? null : prices.Max(p => p.CapturedAt),
            PricesAreClosing: isClosing,
            Unpriced: prices.Count > 0 ? null : ExplainGap(game, lastScanInLeague));
    }

    /// <summary>
    /// Why a game has no price, in the operator's terms.
    ///
    /// A row of dashes is ambiguous between three very different situations — the odds feed is
    /// broken, this fixture was never offered, or the game started before anyone priced it —
    /// and only one of them is worth acting on. Saying which turns a silent gap into a fact.
    ///
    /// <para>
    /// This used to read "pre-match prices are dropped once a game starts", which described the
    /// retention policy rather than the fixture: it was said of every started game, including
    /// the ones whose closing line was sitting in the permanent record unread. A started game
    /// only reaches this now when no close was ever captured for it.
    /// </para>
    /// </summary>
    private static string ExplainGap(Game game, DateTimeOffset? lastScanInLeague)
    {
        var now = DateTimeOffset.UtcNow;

        if (game.StartsAt <= now)
            return "Under way — and no closing line was captured before it started.";

        if (lastScanInLeague is not { } last)
            return "No odds scan has run yet. Pull data → Pull lines.";

        // A pull from days ago "not covering" a fixture that did not exist yet is not a
        // coverage gap. Under manual polling it is the common case, and the fact worth acting
        // on is the age of the last pull, not the fixture.
        var age = now - last;
        if (age >= StalePull)
            return $"No lines pulled in {Ago(age)}. Pull data → Pull lines.";

        return "The last scan did not cover this fixture.";
    }

    /// <summary>
    /// How old a league's newest scan can be before an unpriced row blames the pull rather than
    /// the fixture. A day: automatic polling scans far more often than that, and a manual pull
    /// older than a day predates most of the slate it would be asked about.
    /// </summary>
    public static readonly TimeSpan StalePull = TimeSpan.FromDays(1);

    private static string Ago(TimeSpan age)
        => age.TotalDays >= 2 ? $"{(int)age.TotalDays} days" : $"{(int)age.TotalHours} hours";

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

        var live = (await db.OddsSnapshots
            .Where(s => s.GameId == gameId)
            .GroupBy(s => new { s.Book, s.Market, s.Outcome })
            .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
            .AsNoTracking()
            .ToListAsync(ct))
            .Select(Quote.From)
            .ToList();

        var closing = live.Count > 0
            ? []
            : MarketFirst(await db.ClosingLines
                .Where(c => c.GameId == gameId)
                .Select(c => new ClosedQuote(c, c.Source!.Kind))
                .AsNoTracking()
                .ToListAsync(ct))
                .GetValueOrDefault(gameId) ?? [];

        // Only asked when the row is going to have to explain itself, because the answer is
        // only used to tell "the feed has never run" apart from "the feed skipped this
        // fixture". Passing null unconditionally made every unpriced game claim no scan had
        // ever run — which reads as a broken platform on a desk holding a full slate of prices.
        DateTimeOffset? scanned = live.Count > 0 || closing.Count > 0
            ? null
            : (await LastPullBySportAsync([game.Sport!], ct)).TryGetValue(game.SportId, out var at) ? at : null;

        return Compose(game, live, closing, scanned);
    }

    /// <summary>
    /// One priced outcome, from whichever tier it came out of.
    ///
    /// A scan and a close are the same observation with different lifetimes, and the board's
    /// comparator does not care which table it was read from — so the ranking works on this
    /// rather than on two near-identical entity types with a duplicated sort between them.
    /// </summary>
    private readonly record struct Quote(
        string Book,
        string Market,
        string Outcome,
        decimal? Line,
        int PriceAmerican,
        DateTimeOffset CapturedAt)
    {
        public static Quote From(OddsSnapshot s)
            => new(s.Book, s.Market, s.Outcome, s.Line, s.PriceAmerican, s.CapturedAt);

        public static Quote From(ClosingLine c)
            => new(c.Book, c.Market, c.Outcome, c.Line, c.PriceAmerican, c.CapturedAt);
    }

    /// <summary>Both sides of one market, each with its own best book.</summary>
    private static MarketPair BestPair(List<Quote> prices, string market, Game game, bool closing)
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

        // Both sides paired once, so every offer on either side reads its fair price and each
        // book's margin from the same pairing.
        var fair = FairMarket.Build(market, first, second,
            inMarket.Select(p => new SidePrice(p.Book, p.Outcome, p.Line, p.PriceAmerican)));

        return new MarketPair(
            market,
            Best(inMarket.Where(p => p.Outcome == first).ToList(), market, closing, fair),
            Best(inMarket.Where(p => p.Outcome == second).ToList(), market, closing, fair));
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
    private static BestOffer? Best(List<Quote> side, string market, bool closing, FairMarket fair)
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
                OddsMath.ImpliedProbability(p.PriceAmerican))
            {
                Ev = fair.Ev(p.Outcome, p.Line, p.PriceAmerican),
                Hold = fair.Hold(p.Book, p.Outcome, p.Line),
            })
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
            IsClosing: closing,
            Rungs: rungs)
        {
            Fair = fair.For(best.Outcome, best.Line),
            Ev = fair.Ev(best.Outcome, best.Line, best.PriceAmerican),
        };
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
    bool PricesAreClosing,
    string? Unpriced)
{
    public bool HasPrices => BookCount > 0;

    /// <summary>
    /// How old the newest price on this row is.
    ///
    /// Only meaningful for the live market. A closing line is old by construction — it was
    /// taken before first pitch and will never move again — so reporting its age as staleness
    /// would send an operator chasing a number that is already final.
    /// </summary>
    public TimeSpan? Age
        => PricesAreClosing || PricedAt is not { } at ? null : DateTimeOffset.UtcNow - at;

    /// <summary>Whether the game has a result to show, however partial.</summary>
    public bool HasScore => Scoreline.Has(Game);

    /// <summary>
    /// The most value any book's live price on this row offers against the fair price, or null
    /// where nothing can be scored. Read across every book rather than each side's best price:
    /// the book with the most value is not always the one with the best number. A close is left
    /// out — there is nothing left to take.
    /// </summary>
    public double? BestEv
        => new[] { Moneyline.First, Moneyline.Second, Spread.First, Spread.Second, Total.First, Total.Second }
            .Where(o => o is { IsClosing: false })
            .SelectMany(o => o!.Rungs)
            .Max(r => r.Ev);
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
    bool IsClosing,
    IReadOnlyList<BookPrice> Rungs)
{
    /// <summary>
    /// The side's fair price on the best offer's number, or null where there is none — no
    /// Pinnacle and fewer than two books, or a number nobody else priced (see
    /// <see cref="FairValue"/>).
    /// </summary>
    public FairPrice? Fair { get; init; }

    /// <summary>Expected return per unit at the best offer's price, against <see cref="Fair"/>.</summary>
    public double? Ev { get; init; }

    /// <summary>
    /// The book with the most value on this side, which is not always the best price: a book
    /// hanging an extra half point ranks first on line and has no fair price to be scored by,
    /// while a better price on the reference's number does. Null when no rung can be scored.
    /// </summary>
    public BookPrice? BestValue
        => Rungs.Where(r => r.Ev is not null).OrderByDescending(r => r.Ev).FirstOrDefault();
}

/// <summary>One book's standing on a side.</summary>
public record BookPrice(string Book, int PriceAmerican, decimal? Line, double Implied)
{
    /// <summary>Expected return per unit at this book's price, where its number has a fair price.</summary>
    public double? Ev { get; init; }

    /// <summary>This book's margin on the pair, where it quotes both sides on the same number.</summary>
    public double? Hold { get; init; }
}
