namespace LineOps.Core.Entities;

/// <summary>
/// Canonical market keys. Deliberately strings, not an enum, on the entity:
/// player props and futures slot in later with no schema change.
/// </summary>
public static class Markets
{
    public const string Moneyline = "h2h";
    public const string Spread = "spread";
    public const string Total = "total";

    public static readonly string[] V1 = [Moneyline, Spread, Total];
}

/// <summary>
/// An observation of one priced outcome at one book at one instant.
/// Append-only: rows are never updated, so line movement is reconstructible.
/// </summary>
public class OddsSnapshot
{
    public long Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Sportsbook key, e.g. "draftkings".</summary>
    public string Book { get; set; } = string.Empty;

    /// <summary>See <see cref="Markets"/>. Text so future markets need no migration.</summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>Which side: team abbrev, "over"/"under", etc.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Handicap or total. Null for moneyline.</summary>
    public decimal? Line { get; set; }

    public int PriceAmerican { get; set; }

    /// <summary>Null for v1 team markets; populated when player props arrive.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public long IngestionRunId { get; set; }
}

/// <summary>
/// The line as it stood when the game started — the only odds record kept permanently.
///
/// <para>
/// <see cref="OddsSnapshot"/> is a scan tier now: it holds the live market while a game is
/// still ahead of us, and it is deleted once the game is under way. What survives is this,
/// one row per game, book, market and outcome, promoted at first pitch when the line is set.
/// That is the observation everything downstream actually needs — closing-line value, and
/// "what did the market think of this matchup" — and it costs a few hundred rows a day
/// instead of a stream that grows with poll frequency for ever.
/// </para>
///
/// <para>
/// Unlike <see cref="OddsSnapshot"/> this table is not partitioned, so it has a plain primary
/// key and <i>can</i> be the target of a foreign key — the constraint that forced
/// <c>ADR 0002</c> does not apply here.
/// </para>
/// </summary>
public class ClosingLine
{
    public long Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public string Book { get; set; } = string.Empty;

    /// <summary>See <see cref="Markets"/>.</summary>
    public string Market { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public decimal? Line { get; set; }

    public int PriceAmerican { get; set; }

    /// <summary>Null for team markets; populated when player props arrive.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>
    /// When the observation that became the close was taken. Not the same as
    /// <see cref="PromotedAt"/>: a book that stops moving an hour before first pitch has a
    /// close an hour old, and the gap is worth being able to see.
    /// </summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>When promotion ran. Always at or after the game's start.</summary>
    public DateTimeOffset PromotedAt { get; set; }
}

/// <summary>Raw per-game stats payload. jsonb because stat shapes differ per sport and source.</summary>
public class StatSnapshot
{
    public long Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public int SourceId { get; set; }
    public Source? Source { get; set; }

    public string Payload { get; set; } = "{}";
    public DateTimeOffset CapturedAt { get; set; }
    public long IngestionRunId { get; set; }
}

/// <summary>A player's box-score line for one game. Upserted on backfill.</summary>
public class PlayerGameStat
{
    public long Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>jsonb: pts/reb/ast, pass yds, shots on goal… per sport.</summary>
    public string StatLine { get; set; } = "{}";

    public DateTimeOffset CapturedAt { get; set; }
    public long IngestionRunId { get; set; }
}
