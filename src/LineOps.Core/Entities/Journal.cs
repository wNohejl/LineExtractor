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

    public string? Note { get; set; }

    /// <summary>Groups legs of the same parlay. Null for straight entries.</summary>
    public Guid? ParlayGroupId { get; set; }

    /// <summary>Profit relative to stake. Negative on a loss, zero on push/pending.</summary>
    public decimal NetReturn => Result switch
    {
        EntryResult.Win => (Payout ?? 0m) - Stake,
        EntryResult.Loss => -Stake,
        _ => 0m
    };

    /// <summary>Entries that have settled and therefore count toward ROI.</summary>
    public bool IsSettled => Result is EntryResult.Win or EntryResult.Loss or EntryResult.Push;
}
