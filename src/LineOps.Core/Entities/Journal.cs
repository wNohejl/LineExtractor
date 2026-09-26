namespace LineOps.Core.Entities;

public enum EntryResult
{
    Pending,
    Win,
    Loss,
    Push,
    Void
}

/// <summary>
/// A record of a wager the user placed elsewhere, logged here for performance analytics.
/// LineOps does not place wagers and has no integration with any wagering system —
/// this entity is a ledger row, never an instruction.
/// </summary>
public class JournalEntry
{
    public int Id { get; set; }

    /// <summary>Null when the entry references something we don't have a game row for (e.g. a future).</summary>
    public int? GameId { get; set; }
    public Game? Game { get; set; }

    public string Market { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Set for player-based entries once props are ingested.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>
    /// Lets any bet be logged today — props, parlay legs, futures — before those markets
    /// have an odds feed. CLV simply stays null until a matching feed exists.
    /// </summary>
    public string? FreeTextMarket { get; set; }

    public string Book { get; set; } = string.Empty;

    /// <summary>The handicap/total taken, as it stood at placement.</summary>
    public decimal? LineTaken { get; set; }

    public int PriceTaken { get; set; }
    public decimal Stake { get; set; }
    public DateTimeOffset PlacedAt { get; set; }

    public EntryResult Result { get; set; } = EntryResult.Pending;

    /// <summary>Total returned including stake. Null while pending.</summary>
    public decimal? Payout { get; set; }

    /// <summary>
    /// When the result was recorded. The bankroll moves when a bet settles, not when it is
    /// placed, so this — not <see cref="PlacedAt"/> — orders the curve.
    /// </summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>
    /// The <c>ClosingLine</c> this entry was priced against — the line at first pitch for the
    /// same book, market and outcome.
    ///
    /// Still a plain identifier rather than a navigation property. It used to point at an
    /// <c>OddsSnapshot</c>, which could not be a foreign-key target because that table is
    /// partitioned; it now points at <c>ClosingLines</c>, which could be. The reference is
    /// left unenforced anyway because <see cref="ClosingPrice"/> below is what CLV actually
    /// reads, and an FK would tie the retention of a settled entry to a table it no longer
    /// depends on.
    /// </summary>
    public long? ClosingSnapshotId { get; set; }

    /// <summary>When the closing observation was taken.</summary>
    public DateTimeOffset? ClosingCapturedAt { get; set; }

    /// <summary>The closing price, denormalised at resolution time so CLV survives pruning.</summary>
    public int? ClosingPrice { get; set; }

    /// <summary>
    /// The handicap or total the close was quoted at, for the same outcome. A price only means
    /// something at its number: -110 at -2.5 is not -110 at -1.5, and comparing the two as if
    /// they were scored a point of line value as zero. Null for a moneyline.
    /// </summary>
    public decimal? ClosingPoints { get; set; }

    /// <summary>
    /// The book whose close this entry was compared with. Usually the entry's own; when that
    /// book was not tracked, whichever close settlement fell back to — a weaker comparison,
    /// and one the desk should say it made.
    /// </summary>
    public string? ClosingBook { get; set; }

    /// <summary>
    /// The closing market's fair chance of this side at the number it was taken on — the close
    /// with the margin taken out (<see cref="Analytics.FairValue"/>), Pinnacle's where it closed.
    ///
    /// <para>
    /// Comparing the price taken with one book's closing price counts that book's margin as the
    /// bettor's loss: -110 into a -110 close reads as zero, though the fair price was +100 and
    /// the bet cost 4.5 cents. Valuing the price taken at the fair close is the reading that
    /// does not flatter or punish the book's cut, and summed over a journal it is the best
    /// available estimate of edge long before the results can say. Null where the close had no
    /// fair price at the entry's number — one book, or a line that moved off it.
    /// </para>
    /// </summary>
    public double? ClosingFairProbability { get; set; }

    /// <summary>What <see cref="ClosingFairProbability"/> was read from: "pinnacle" or "consensus".</summary>
    public string? ClosingFairBasis { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// The parlay this entry is a leg of. Null for a straight bet. A leg carries no money of its
    /// own — its stake is zero and the parlay holds the stake and the payout — but it is graded,
    /// and priced against the close, like any other selection.
    /// </summary>
    public Guid? ParlayGroupId { get; set; }
    public Parlay? Parlay { get; set; }

    public bool IsParlayLeg => ParlayGroupId is not null;

    /// <summary>Profit relative to stake. Negative on a loss, zero on push, void and pending.</summary>
    public decimal NetReturn => Result switch
    {
        EntryResult.Win => (Payout ?? 0m) - Stake,
        EntryResult.Loss => -Stake,
        _ => 0m
    };

    /// <summary>
    /// No longer waiting on anything: graded, or voided. A void used to be left out, so a voided
    /// entry kept its "Settle…" menu and was counted as pending for ever.
    /// </summary>
    public bool IsSettled => IsGraded || Result == EntryResult.Void;

    /// <summary>
    /// Settled with action — won, lost or pushed — and therefore counted toward ROI. A void is
    /// settled but had no action: its stake came back and was never really risked, so counting
    /// it as staked would dilute the return on the bets that were.
    /// </summary>
    public bool IsGraded => Result is EntryResult.Win or EntryResult.Loss or EntryResult.Push;
}

/// <summary>
/// A parlay logged in the journal: one stake, one payout, several legs.
///
/// <para>
/// Its own row rather than a convention over its legs. The legs used to share a group id and
/// nothing else, which left nowhere to put the stake without copying it onto every leg — and
/// then every reader that summed stakes would have had to know to fold them back together.
/// </para>
/// </summary>
public class Parlay
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Book { get; set; } = string.Empty;
    public decimal Stake { get; set; }

    /// <summary>
    /// The combined price the book quoted, when it did. Books price correlated and boosted
    /// parlays their own way, so where it is known it is what a clean win pays; the product of
    /// the legs' prices is the fallback, and the only way to reprice after a push.
    /// </summary>
    public int? PriceQuoted { get; set; }

    public DateTimeOffset PlacedAt { get; set; }

    public EntryResult Result { get; set; } = EntryResult.Pending;

    /// <summary>Total returned including stake. Null while pending.</summary>
    public decimal? Payout { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public string? Note { get; set; }

    public List<JournalEntry> Legs { get; set; } = [];

    public decimal NetReturn => Result switch
    {
        EntryResult.Win => (Payout ?? 0m) - Stake,
        EntryResult.Loss => -Stake,
        _ => 0m
    };

    public bool IsGraded => Result is EntryResult.Win or EntryResult.Loss or EntryResult.Push;

    public bool IsSettled => IsGraded || Result == EntryResult.Void;
}

/// <summary>A named setting the operator owns, such as the starting bankroll. Travels with the data.</summary>
public class AppSetting
{
    public const string StartingBankroll = "journal.starting_bankroll";

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
