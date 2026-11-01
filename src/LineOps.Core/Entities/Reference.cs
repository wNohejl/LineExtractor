namespace LineOps.Core.Entities;

/// <summary>A league we track, e.g. NFL, NBA, MLB, NHL.</summary>
public class Sport
{
    public int Id { get; set; }

    /// <summary>Stable slug used in config and source mappings, e.g. "nfl".</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public List<Team> Teams { get; set; } = [];
    public List<Game> Games { get; set; } = [];
}

public class Team
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Abbrev { get; set; } = string.Empty;

    /// <summary>Per-source identifiers, keyed by source key. Cross-source entity resolution lives here.</summary>
    public Dictionary<string, string> ExternalIds { get; set; } = [];
}

public class Player
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    /// <summary>Null when the player is a free agent or unassigned.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }

    /// <summary>active | injured | inactive — as reported by the stats source.</summary>
    public string Status { get; set; } = "active";

    public Dictionary<string, string> ExternalIds { get; set; } = [];
}

public enum SourceKind
{
    Odds,
    Stats
}

/// <summary>An external data provider. Rate limits and budgets are enforced against these rows.</summary>
public class Source
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SourceKind Kind { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Free-tier ceiling. Null means unmetered.</summary>
    public int? RateLimitPerHour { get; set; }
    public int? RateLimitPerDay { get; set; }

    /// <summary>Monthly credit budget for credit-billed providers (e.g. The Odds API).</summary>
    public int? MonthlyCreditBudget { get; set; }

    /// <summary>Dev-only failure injection (Phase 5). Never enabled in production config.</summary>
    public string? FailureMode { get; set; }
}

public enum GameStatus
{
    Scheduled,
    Live,
    Final,
    Postponed
}

/// <summary>
/// Which part of a season a game belongs to. Stamped by the provider at ingest — ESPN says it
/// on every event — and derived by rule (<c>SeasonCalendar</c>) for rows that predate the column.
/// </summary>
public enum SeasonType
{
    Regular,
    Postseason,
    Preseason,
    Exhibition
}

public class Game
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    public int HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }
    public int AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// The season this game belongs to, by the year the sport names it — an NFL playoff in
    /// February 2026 is season 2025. Filled from the provider's own stamp where it gives one
    /// (<c>CanonicalGame.SeasonYear</c>), otherwise from <c>SeasonCalendar</c>.
    /// </summary>
    public int SeasonYear { get; set; }

    public SeasonType SeasonType { get; set; } = SeasonType.Regular;

    public GameStatus Status { get; set; } = GameStatus.Scheduled;

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    /// <summary>
    /// The starting pitchers MLB has announced, as MLB names them — the single most useful
    /// handicapping fact on a baseball slate, and one the schedule call returned and nothing
    /// kept. Null where none is announced yet; it changes when a starter is scratched.
    /// </summary>
    public string? HomeProbablePitcher { get; set; }
    public string? AwayProbablePitcher { get; set; }

    /// <summary>
    /// Game 1 or 2 of a doubleheader, as MLB numbers it; null for a single game. Two games with
    /// the same teams on the same day are told apart by this rather than by start time, which
    /// a rain delay can reorder.
    /// </summary>
    public int? DoubleHeaderGame { get; set; }

    // ---- The state of play (spec 2026-10-24 §5) ------------------------------------------------
    //
    // What ESPN's scoreboard says about a game while it is on, kept so the desk can show the clock,
    // the down and the last play rather than a score and the word "Live". Written change-only by
    // the resolver: the live poll runs every few seconds and a write is what raises the desk's
    // change notice, so a poll that saw nothing new must write nothing. The situation fields —
    // possession through Situation — clear once the game is no longer in play; the period, clock,
    // detail and linescore stay, because "Final/OT" and the score by quarter are still true.

    /// <summary>
    /// The column lengths of the state of play's short strings. The context maps the columns from
    /// these and the adapter cuts ESPN's strings to them, so an over-long status line is trimmed
    /// rather than failing the save it rides in.
    /// </summary>
    public const int DisplayClockLength = 16;
    public const int StatusDetailLength = 64;
    public const int DownDistanceTextLength = 64;
    public const int PossessionTextLength = 32;

    /// <summary>Quarter, inning or round. Null before the start.</summary>
    public int? Period { get; set; }

    /// <summary>The game clock as ESPN prints it — "4:12".</summary>
    public string? DisplayClock { get; set; }

    /// <summary>The same clock as seconds remaining in the period, for anything that counts down.</summary>
    public int? ClockSeconds { get; set; }

    /// <summary>ESPN's <c>status.type.shortDetail</c>: "Q3 4:12", "Halftime", "Final/OT".</summary>
    public string? StatusDetail { get; set; }

    /// <summary>
    /// Our <see cref="Team.Id"/> for the side with the ball — always this game's home or away,
    /// resolved from ESPN's team id; null when ESPN names neither.
    /// </summary>
    public int? PossessionTeamId { get; set; }

    public int? Down { get; set; }
    public int? Distance { get; set; }

    /// <summary>
    /// ESPN's <c>situation.yardLine</c>, as given: yards from the <b>home</b> team's goal line, on
    /// one fixed frame for the whole field — not yards to go, and not relative to the side with the
    /// ball. Checked against <c>possessionText</c> across the three games live on 1 Nov 2026: LAC
    /// (away) with the ball at "LAC 20" in LAC @ LAR read 80; DEN (home) at "KC 33" in KC @ DEN read
    /// 67; MIA (home) at "MIA 29" in NE @ MIA read 29. The drive's start in the same payload agrees
    /// ("LAR 28" is 28). Yards to the end zone is therefore <c>100 - YardLine</c> when the home side
    /// has the ball and <c>YardLine</c> when the away side does.
    /// </summary>
    public int? YardLine { get; set; }

    /// <summary>"2nd &amp; 6 at NE 34".</summary>
    public string? DownDistanceText { get; set; }

    /// <summary>Where the ball is, in ESPN's own words — "NE 34".</summary>
    public string? PossessionText { get; set; }

    public bool? IsRedZone { get; set; }
    public int? HomeTimeouts { get; set; }
    public int? AwayTimeouts { get; set; }

    /// <summary>ESPN's text for the last play — "D.Maye pass incomplete short left…".</summary>
    public string? LastPlay { get; set; }

    /// <summary>
    /// Points by period as jsonb — <c>{"home":[10,7],"away":[3,0]}</c>, first period first. A
    /// string rather than a typed column for the reason <c>PlayerGameStat.StatLine</c> is one:
    /// the shape is small, read whole and never queried into.
    /// </summary>
    public string? Linescore { get; set; }

    /// <summary>
    /// What is sport-specific about the situation, as jsonb: baseball's <c>balls</c>,
    /// <c>strikes</c>, <c>outs</c>, <c>onFirst</c>/<c>onSecond</c>/<c>onThird</c>, <c>batter</c>
    /// and <c>pitcher</c>. Null for football, whose situation is the columns above.
    /// </summary>
    public string? Situation { get; set; }

    /// <summary>When any of the state of play last changed. Not touched by a poll that saw nothing new.</summary>
    public DateTimeOffset? LiveUpdatedAt { get; set; }

    public Dictionary<string, string> ExternalIds { get; set; } = [];
}
