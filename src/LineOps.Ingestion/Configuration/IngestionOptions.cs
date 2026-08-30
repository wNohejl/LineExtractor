namespace LineOps.Ingestion.Configuration;

public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Sports polled by the scheduler, by <see cref="Core.Entities.Sport.Key"/>.
    ///
    /// Empty by default, and read through <see cref="EffectiveSports"/>. That is not a style
    /// choice: the configuration binder <i>appends</i> to an array that already holds a default
    /// rather than replacing it, so a non-empty default cannot be narrowed from config —
    /// asking for just MLB would silently give you all four leagues back. An empty default is
    /// the only one config can fully control.
    /// </summary>
    public string[] Sports { get; set; } = [];

    /// <summary>The leagues to poll, falling back to the majors when config names none.</summary>
    public string[] EffectiveSports => Sports.Length > 0 ? Sports : DefaultSports;

    public static readonly string[] DefaultSports = ["nfl", "nba", "mlb", "nhl"];

    /// <summary>Only snapshot games starting inside this window, to conserve free-tier requests.</summary>
    public TimeSpan MovementWindow { get; set; } = TimeSpan.FromHours(36);

    /// <summary>How often the scheduler loop wakes to check for due jobs.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>When true the worker runs every job once at startup — useful for drills.</summary>
    public bool RunOnStartup { get; set; }

    public SourceOptions OddsApiIo { get; set; } = new();
    public SourceOptions TheOddsApi { get; set; } = new();
    public SourceOptions BallDontLie { get; set; } = new();
    public SourceOptions Espn { get; set; } = new() { Enabled = true };

    /// <summary>
    /// MLB's own schedule API: free, unauthenticated, and the issuer of the ids baseball runs on.
    /// On by default because it is the entity spine rather than one more feed — every other
    /// source resolves into its <c>gamePk</c> and MLBAM team and player ids.
    /// </summary>
    public SourceOptions MlbStatsApi { get; set; } = new()
    {
        Enabled = true,
        // Unmetered and unpublished, which is a reason for more care rather than less.
        RequestDelay = TimeSpan.FromMilliseconds(250)
    };

    public BackfillOptions Backfill { get; set; } = new();

    public OddsRetentionOptions OddsRetention { get; set; } = new();

    public LinePollingOptions LinePolling { get; set; } = new();

    /// <summary>When to ask ESPN for schedules and results — distinct from <see cref="Espn"/>,
    /// which is the adapter's own connection settings.</summary>
    public EspnScheduleOptions GamePasses { get; set; } = new();
}

/// <summary>Who decides when lines are fetched.</summary>
public enum LinePollingMode
{
    /// <summary>Only when an operator presses <b>Pull lines</b>. Nothing is spent unasked.</summary>
    Manual,

    /// <summary>The scheduler scans on the derived cadence as well as on request.</summary>
    Scheduled
}

/// <summary>
/// How the line-scan cadence is derived from the provider's allowance.
///
/// The numbers here are the safety margins, not the cadence itself — see
/// <c>LinePollPlanner</c>, which computes the interval from what the day has left.
/// </summary>
public class LinePollingOptions
{
    /// <summary>
    /// Whether the scheduler may spend on lines by itself.
    ///
    /// <para>
    /// A named mode rather than a bool, because <c>Enabled = false</c> reads as "line polling is
    /// off" when the operator can still pull lines whenever they like — it is only the
    /// <i>unattended</i> spending that stops. The distinction is the whole point on a metered
    /// feed, so the setting says which it means.
    /// </para>
    ///
    /// <para>
    /// <b>Manual by default.</b> On a 500-credit month, a scheduler that polls on its own spends
    /// the allowance on whatever happens to be on the slate rather than on what an operator is
    /// actually working — and does it silently, in the background, including overnight and while
    /// nobody is at the desk. Automatic spending should be something switched on deliberately by
    /// someone who has read the price, not the state a fresh clone starts in.
    /// </para>
    /// </summary>
    public LinePollingMode Mode { get; set; } = LinePollingMode.Manual;

    /// <summary>True when the scheduler is permitted to scan without being asked.</summary>
    public bool RunsUnattended => Mode == LinePollingMode.Scheduled;

    /// <summary>
    /// Requests one scan costs per sport, per source. Two for odds-api.io: one <c>/events</c>
    /// and one batched <c>/odds/multi</c>, which is why the cost does not grow with the slate.
    /// </summary>
    public int RequestsPerSportPerScan { get; set; } = 2;

    /// <summary>
    /// Daily requests held back from the pacing maths.
    ///
    /// Without a reserve the cadence spends the quota to the last request, and the first manual
    /// pull or retry of the evening is refused. This is what makes the guard a backstop rather
    /// than a routine outcome.
    /// </summary>
    public int DailyReserve { get; set; } = 50;

    /// <summary>Hourly equivalent of <see cref="DailyReserve"/>.</summary>
    public int HourlyReserve { get; set; } = 10;

    /// <summary>
    /// Credits one scan costs per sport, per source, for providers that bill in credits rather
    /// than requests.
    ///
    /// The Odds API charges <c>markets x regions</c> per call, so a moneyline-and-spread scan of
    /// one region is two — which is why the market list is kept short deliberately. This is a
    /// separate number from <see cref="RequestsPerSportPerScan"/> because they measure different
    /// things: that one is HTTP calls, this one is what the provider bills for them.
    /// </summary>
    public int CreditsPerSportPerScan { get; set; } = 2;

    /// <summary>
    /// Credits held back from the monthly pacing maths, for the same reason as
    /// <see cref="DailyReserve"/> — an operator pressing <b>Pull lines</b> on the last day of the
    /// month should not find the allowance already spent to the last credit by the scheduler.
    /// </summary>
    public int MonthlyCreditReserve { get; set; } = 100;

    /// <summary>
    /// Never scan faster than this, however generous the tier. Books do not reprice every few
    /// seconds, so past a point the extra requests buy sampling noise rather than movement.
    /// </summary>
    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Back-off when the budget is spent, or when nothing is metered.</summary>
    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromHours(3);
}

/// <summary>
/// When to ask ESPN for schedules and results.
///
/// Driven by game state rather than a fixed hour: what makes a slate worth refreshing is a
/// game about to start, and what makes results worth fetching is a game that should have
/// finished. A clock-based job either runs when nothing has changed or misses the change.
/// </summary>
public class EspnScheduleOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Refresh the day's slate when the next game starts within this. Catches late start-time
    /// moves, postponements and scratches while they still matter.
    /// </summary>
    public TimeSpan PreGameLead { get; set; } = TimeSpan.FromHours(3);

    /// <summary>Don't re-scrape the slate more often than this, however many games are due.</summary>
    public TimeSpan SlateRefresh { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long after a start to expect a result. Long enough to cover a normal game plus
    /// extras, so the results pass is not asking for box scores that do not exist yet.
    /// </summary>
    public TimeSpan ResultsAfterStart { get; set; } = TimeSpan.FromHours(4);

    /// <summary>Gap between results sweeps while games are still outstanding.</summary>
    public TimeSpan ResultsRetry { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gap between score refreshes while a game is live. Tighter than <see cref="SlateRefresh"/>
    /// because a live score is stale in minutes, not hours — and separate from it because "a
    /// game is in progress" is a different reason to poll than "a game is about to start", with
    /// its own cadence.
    /// </summary>
    public TimeSpan LivePoll { get; set; } = TimeSpan.FromSeconds(90);
}

/// <summary>
/// How long scanned odds are kept before the closing line replaces them.
///
/// The shape of the policy is the point: odds are working state until first pitch and a single
/// permanent number afterwards. Nothing here decides <i>what</i> is kept — that is
/// <c>ClosingLine</c> — only when the scans behind it stop being useful.
/// </summary>
public class OddsRetentionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// No odds scan is kept from before this date, promoted or not.
    ///
    /// This is the date the two-tier policy took effect. Scans older than it predate the
    /// closing-line record and cannot be promoted into one, so they are neither live market
    /// nor history — just cost.
    /// </summary>
    public DateOnly HistoryFloor { get; set; } = new(2026, 7, 25);

    /// <summary>
    /// Wait this long after a game's start before taking its close.
    ///
    /// A book still being scanned as the game begins would otherwise have a line a few seconds
    /// stale promoted over the one that lands moments later. The close is only read from scans
    /// taken at or before the start either way, so this delays the decision rather than
    /// widening what counts.
    /// </summary>
    public TimeSpan PromoteAfterStart { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Games promoted per pass, so a first run over a backlog stays bounded.</summary>
    public int PromoteBatchSize { get; set; } = 250;

    /// <summary>How often the scheduler runs promotion and pruning.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// History backfill: walking past days to build a record the scheduler alone would take
/// months of real time to accumulate.
///
/// Every setting here is about spending someone else's free service politely. The one thing
/// that is <i>not</i> configurable is which providers may be used — see
/// <c>HistoryBackfillService</c>, which admits unmetered sources only and refuses metered
/// ones whatever this section says.
/// </summary>
public class BackfillOptions
{
    /// <summary>Start the backfill automatically once the host is up.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How far back to reach, as a day count. Ignored when <see cref="Since"/> is set.
    /// </summary>
    public int Days { get; set; } = 180;

    /// <summary>
    /// Reach back to a fixed date instead of a rolling window.
    ///
    /// A season has a start date, not a length, and a rolling count silently changes what it
    /// covers every day it runs — "180 days" reaches opening day in July and misses it in
    /// September. Setting this pins the target so the walk finishes and stays finished.
    /// </summary>
    public DateOnly? Since { get; set; }

    /// <summary>
    /// Sources to walk, by key. Metered providers are rejected regardless of what appears
    /// here, so naming one is a no-op rather than a way to start spending.
    ///
    /// Empty by default for the binder-append reason described on
    /// <see cref="IngestionOptions.Sports"/>; read through <see cref="EffectiveSources"/>.
    /// </summary>
    public string[] Sources { get; set; } = [];

    /// <summary>The sources to walk, falling back to ESPN — the only free stats port today.</summary>
    public string[] EffectiveSources => Sources.Length > 0 ? Sources : ["espn"];

    /// <summary>
    /// Sports to walk. Defaults to <see cref="IngestionOptions.Sports"/> when left empty.
    /// </summary>
    public string[] Sports { get; set; } = [];

    /// <summary>
    /// Pause between day fetches. ESPN publishes no rate limit, which is a reason to be more
    /// careful rather than less: this is an unofficial endpoint being used as a courtesy.
    /// </summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Consecutive failures against one source before the walk gives up on it. A provider
    /// that has started refusing us should be left alone, not retried 1,400 times.
    /// </summary>
    public int AbortAfterConsecutiveFailures { get; set; } = 8;

    /// <summary>
    /// Re-walk days that previously failed. Off by default so the ordinary case — resuming an
    /// interrupted backfill — never re-fetches a day that already succeeded.
    /// </summary>
    public bool RetryFailedDays { get; set; }
}

public class SourceOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>
    /// Books to request.
    ///
    /// <para>
    /// Variety is the point of paying attention to more than one: a single book's number is a
    /// price, several books' numbers are a market, and the gap between them is where a line
    /// being off shows up. Requesting more books does not cost more requests on odds-api.io —
    /// the whole slate is one <c>/odds/multi</c> call whatever the list — so the ceiling here
    /// is the provider's free tier rather than our budget.
    /// </para>
    ///
    /// <para>
    /// Free tiers do cap this, and a book the tier does not include is quietly absent from the
    /// response rather than an error, so an over-long list degrades instead of failing.
    /// </para>
    ///
    /// <para>
    /// Empty by default and read through <see cref="EffectiveBookmakers"/>, because the binder
    /// appends to a non-empty default instead of replacing it — so a config asking for only
    /// BetMGM would quietly get DraftKings and FanDuel as well.
    /// </para>
    /// </summary>
    public string[] Bookmakers { get; set; } = [];

    /// <summary>The books to request, falling back to two recreational ones.</summary>
    public string[] EffectiveBookmakers
        => Bookmakers.Length > 0 ? Bookmakers : DefaultBookmakers;

    public static readonly string[] DefaultBookmakers = ["draftkings", "fanduel"];

    /// <summary>
    /// Markets to request, by <see cref="Core.Entities.Markets"/> key.
    ///
    /// <para>
    /// This is a cost control, not a preference. A credit-billed provider charges
    /// <c>markets x regions</c> per call, so asking for totals as well as moneyline and spread is
    /// a standing 50% surcharge on every scan for the rest of the month. Narrowing the list is
    /// the single most effective way to make a small allowance last.
    /// </para>
    ///
    /// <para>
    /// Empty by default and read through <see cref="EffectiveMarkets"/>, for the binder-append
    /// reason described on <see cref="IngestionOptions.Sports"/>.
    /// </para>
    /// </summary>
    public string[] Markets { get; set; } = [];

    /// <summary>The markets to request, falling back to every market the platform models.</summary>
    public string[] EffectiveMarkets
        => Markets.Length > 0
            ? Markets.Select(NormaliseMarket).Where(m => m is not null).Distinct().ToArray()!
            : Core.Entities.Markets.V1.ToArray();

    /// <summary>
    /// Accepts what someone editing a config file would actually write.
    ///
    /// The canonical moneyline key is <c>h2h</c> — the provider's own term — which is not the
    /// word anyone reaches for. Left strict, "moneyline" would bind cleanly, reach the adapter
    /// unrecognised, and be sent verbatim: a request that still bills a credit and comes back
    /// with nothing in it. An unknown market is dropped instead, so a typo costs the market
    /// rather than the money.
    /// </summary>
    private static string? NormaliseMarket(string market) => market.Trim().ToLowerInvariant() switch
    {
        "h2h" or "moneyline" or "ml" or "money-line" => Core.Entities.Markets.Moneyline,
        "spread" or "spreads" or "handicap" or "line" => Core.Entities.Markets.Spread,
        "total" or "totals" or "overunder" or "over-under" or "ou" => Core.Entities.Markets.Total,
        _ => null
    };

    /// <summary>
    /// Pause between individual HTTP calls this adapter makes.
    ///
    /// Metered providers do not need this — their ceiling is published and the budget guard
    /// enforces it. An unmetered one does: a single day's box scores is one request per
    /// finished game, which for a full MLB slate is fifteen calls fired back to back. That is
    /// fine once a day and rude several hundred times over during a backfill.
    /// </summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.Zero;
}
