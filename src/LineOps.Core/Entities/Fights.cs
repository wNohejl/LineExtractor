namespace LineOps.Core.Entities;

/// <summary>How a finished fight ended, by what it left the two fighters with.</summary>
public enum BoutOutcome
{
    /// <summary>One fighter won: by knockout, submission, decision or disqualification.</summary>
    Win,

    /// <summary>The judges could not separate them. A two-way moneyline pushes.</summary>
    Draw,

    /// <summary>The fight was ruled to have had no result — an accidental foul, an overturned win. Bets are void.</summary>
    NoContest
}

/// <summary>
/// One fight's place on its card and how it ended, beside the <see cref="Game"/> that carries its
/// two fighters.
///
/// <para>
/// A fight is stored as a game between two fighters (ADR 0021) so the journal, the board and every
/// reader that follows a side work for it unchanged. What a game has no column for lives here: the
/// card it was on, where on that card, the weight and the distance, and the result a score cannot
/// say — the method, the round and the clock. One row per fight game; a team sport has none.
/// </para>
/// </summary>
public class Bout
{
    /// <summary>The fight's game. Also this row's key: a bout is one game's detail, never a second game.</summary>
    public int GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>The card's id at the provider — ESPN's event id. Every bout on one card shares it.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>The card as it is billed: "UFC 300: Pereira vs. Hill".</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>The card's short name, "UFC 300" or "UFC Fight Night". Null where the provider gave none.</summary>
    public string? EventShortName { get; set; }

    /// <summary>Main Card, Prelims or Early Prelims, as the provider names the segment.</summary>
    public string? CardSegment { get; set; }

    /// <summary>The fight's place on the card, counted from the top: 1 is the main event.</summary>
    public int? MatchNumber { get; set; }

    /// <summary>"Lightweight", "Women's Strawweight".</summary>
    public string? WeightClass { get; set; }

    /// <summary>Three, or five for a main event or a title fight. What "went the distance" means.</summary>
    public int ScheduledRounds { get; set; } = 3;

    /// <summary>A belt is on the line.</summary>
    public bool TitleFight { get; set; }

    /// <summary>
    /// Each fighter's record going into this fight, as the provider stated it at the time — "13-4-0".
    /// Home is the red corner. Kept per fight because a record read today describes a later fighter.
    /// </summary>
    public string? HomeRecord { get; set; }
    public string? AwayRecord { get; set; }

    /// <summary>Null until the fight has a result.</summary>
    public BoutOutcome? Outcome { get; set; }

    /// <summary>"KO/TKO", "Submission", "Decision - Split".</summary>
    public string? Method { get; set; }

    /// <summary>What the method was: "Punch", "Rear Naked Choke". Null for a decision.</summary>
    public string? MethodDetail { get; set; }

    /// <summary>The round the fight ended in.</summary>
    public int? EndRound { get; set; }

    /// <summary>
    /// How far into <see cref="EndRound"/> it ended, in seconds — 194 for 3:14. A fight that went
    /// the distance ends at the last round's full 300.
    /// </summary>
    public int? EndSeconds { get; set; }
}

/// <summary>
/// The few facts about a fighter a comparison leads with, and ESPN's own statistics kept whole.
///
/// <para>
/// Deliberately small in columns. A fighter's record, size and stance are what every tale of the
/// tape opens on, so they are typed; everything else ESPN states — the career rates and every
/// recorded fight with its full stat sheet — is held as the provider shaped it, in
/// <see cref="Career"/> and <see cref="History"/>. Nothing ESPN measures is dropped, and nothing
/// earns a column it would fill for one sport only.
/// </para>
///
/// <para>
/// One per fighter, keyed by the fighter's <see cref="Team"/> row (ADR 0021). Refreshed when the
/// fighter has fought since <see cref="RefreshedAt"/>, and taken for any fighter on a card ahead.
/// </para>
/// </summary>
public class FighterProfile
{
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public string? Nickname { get; set; }
    public string? Country { get; set; }

    /// <summary>The gym the fighter trains out of, as ESPN names it.</summary>
    public string? Association { get; set; }

    public string? Stance { get; set; }
    public string? WeightClass { get; set; }
    public decimal? HeightInches { get; set; }
    public decimal? ReachInches { get; set; }
    public DateOnly? BornOn { get; set; }

    /// <summary>Wins-losses-draws as ESPN keeps it, "13-4-0" — the professional record, not only the UFC one.</summary>
    public string? Record { get; set; }

    /// <summary>
    /// jsonb: ESPN's career figures — the rates (significant strikes landed per minute, accuracy,
    /// takedown average and accuracy, submission average, finish percentages) and the record's
    /// splits (wins and losses by knockout and by submission, title fights).
    /// </summary>
    public string Career { get; set; } = "{}";

    /// <summary>
    /// jsonb: every fight ESPN has on record for this fighter, newest first — date, card,
    /// opponent, result, method, round and clock — each with ESPN's full per-fight stat sheet
    /// where it kept one. Fights from before UFC 300, and outside the UFC, are here too.
    /// </summary>
    public string History { get; set; } = "[]";

    public DateTimeOffset RefreshedAt { get; set; }
}
