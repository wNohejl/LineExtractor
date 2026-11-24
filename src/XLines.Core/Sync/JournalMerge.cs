using XLines.Core.Entities;

namespace XLines.Core.Sync;

/// <summary>What an import does with one row of the file.</summary>
public enum MergeAction
{
    /// <summary>Not here, and not deleted here more recently: added.</summary>
    Insert,

    /// <summary>Here, and the file's copy is newer: replaced by it.</summary>
    Update,

    /// <summary>Here, and the file deleted it after this copy last changed: deleted.</summary>
    Delete,

    /// <summary>The same here, or newer here, or deleted here after the file's copy changed: left alone.</summary>
    Unchanged,

    /// <summary>Changed on both machines at the same moment to different values: left alone, and said.</summary>
    Conflict
}

/// <summary>One copy of a row: when it last changed and what it holds.</summary>
public readonly record struct Versioned<T>(DateTime UpdatedAtUtc, T Content);

/// <summary>
/// An entry's content in this database's terms: everything a merge compares, with the game and
/// player as this database's ids once the file's references are resolved. The row id, the
/// portable id, the timestamp and the close's per-database row id are not content.
/// </summary>
public sealed record EntryContent(
    int? GameId,
    int? PlayerId,
    Guid? ParlayId,
    string Market,
    string Outcome,
    string? FreeTextMarket,
    string Book,
    decimal? LineTaken,
    decimal PriceTaken,
    bool ManualPrice,
    decimal Stake,
    DateTimeOffset PlacedAt,
    EntryResult Result,
    decimal? Payout,
    DateTimeOffset? SettledAt,
    DateTimeOffset? ClosingCapturedAt,
    int? ClosingPrice,
    decimal? ClosingPoints,
    string? ClosingBook,
    double? ClosingFairProbability,
    string? ClosingFairBasis,
    string? Note)
{
    public static EntryContent Of(JournalEntry e) => new(
        e.GameId, e.PlayerId, e.ParlayGroupId, e.Market, e.Outcome, e.FreeTextMarket, e.Book, e.LineTaken,
        e.PriceTaken, e.ManualPrice, e.Stake, e.PlacedAt, e.Result, e.Payout, e.SettledAt, e.ClosingCapturedAt,
        e.ClosingPrice, e.ClosingPoints, e.ClosingBook, e.ClosingFairProbability, e.ClosingFairBasis, e.Note);

    /// <summary>The file's entry with its references resolved to this database's ids (null where they did not resolve).</summary>
    public static EntryContent Of(JournalFileEntry e, int? gameId, int? playerId) => new(
        gameId, playerId, e.ParlayId, e.Market, e.Outcome, e.FreeTextMarket, e.Book, e.LineTaken,
        e.PriceTaken, e.ManualPrice, e.Stake, e.PlacedAt, e.Result, e.Payout, e.SettledAt, e.ClosingCapturedAt,
        e.ClosingPrice, e.ClosingPoints, e.ClosingBook, e.ClosingFairProbability, e.ClosingFairBasis, e.Note);

    /// <summary>Writes this content onto an entry. The close's row id is the caller's: it is this database's.</summary>
    public void ApplyTo(JournalEntry e)
    {
        e.GameId = GameId;
        e.PlayerId = PlayerId;
        e.ParlayGroupId = ParlayId;
        e.Market = Market;
        e.Outcome = Outcome;
        e.FreeTextMarket = FreeTextMarket;
        e.Book = Book;
        e.LineTaken = LineTaken;
        e.PriceTaken = PriceTaken;
        e.ManualPrice = ManualPrice;
        e.Stake = Stake;
        e.PlacedAt = PlacedAt;
        e.Result = Result;
        e.Payout = Payout;
        e.SettledAt = SettledAt;
        e.ClosingCapturedAt = ClosingCapturedAt;
        e.ClosingPrice = ClosingPrice;
        e.ClosingPoints = ClosingPoints;
        e.ClosingBook = ClosingBook;
        e.ClosingFairProbability = ClosingFairProbability;
        e.ClosingFairBasis = ClosingFairBasis;
        e.Note = Note;
    }
}

/// <summary>A parlay's content: its money, book and result. Its legs are entries and merge as entries.</summary>
public sealed record ParlayContent(
    string Book,
    decimal Stake,
    decimal? PriceQuoted,
    DateTimeOffset PlacedAt,
    EntryResult Result,
    decimal? Payout,
    DateTimeOffset? SettledAt,
    string? Note)
{
    public static ParlayContent Of(Parlay p) => new(p.Book, p.Stake, p.PriceQuoted, p.PlacedAt, p.Result, p.Payout, p.SettledAt, p.Note);

    public static ParlayContent Of(JournalFileParlay p) => new(p.Book, p.Stake, p.PriceQuoted, p.PlacedAt, p.Result, p.Payout, p.SettledAt, p.Note);

    public void ApplyTo(Parlay p)
    {
        p.Book = Book;
        p.Stake = Stake;
        p.PriceQuoted = PriceQuoted;
        p.PlacedAt = PlacedAt;
        p.Result = Result;
        p.Payout = Payout;
        p.SettledAt = SettledAt;
        p.Note = Note;
    }
}

/// <summary>
/// The merge's decisions, as pure functions of the two copies (ADR 0025). The import reads the
/// rows and writes the outcome; everything it decides is decided here, where a test can say so.
///
/// <para>
/// Last writer wins, by the whole row: the copy with the newer <c>UpdatedAtUtc</c> replaces the
/// other. Two copies changed at the same microsecond to different values cannot be ordered, so
/// neither is chosen. A deletion is a row too: newer than a copy, it deletes it; older, the copy
/// survives, because it was changed after the other machine deleted it.
/// </para>
/// </summary>
public static class JournalMerge
{
    /// <summary>
    /// A row the file holds, against this database's copy (if any) and this database's record of
    /// deleting it (if any).
    /// </summary>
    public static MergeAction Row<T>(Versioned<T>? local, DateTime? localDeletedAtUtc, Versioned<T> incoming)
    {
        if (local is not { } here)
            return localDeletedAtUtc is { } deleted && deleted >= incoming.UpdatedAtUtc
                ? MergeAction.Unchanged
                : MergeAction.Insert;

        if (EqualityComparer<T>.Default.Equals(here.Content, incoming.Content))
            return MergeAction.Unchanged;

        return incoming.UpdatedAtUtc.CompareTo(here.UpdatedAtUtc) switch
        {
            > 0 => MergeAction.Update,
            < 0 => MergeAction.Unchanged,
            _ => MergeAction.Conflict
        };
    }

    /// <summary>A deletion the file holds, against this database's copy, if it still has one.</summary>
    public static MergeAction Tombstone<T>(Versioned<T>? local, DateTime deletedAtUtc)
        => local is { } here && deletedAtUtc > here.UpdatedAtUtc ? MergeAction.Delete : MergeAction.Unchanged;

    /// <summary>
    /// An entry: <see cref="Row{T}"/>, and one more case. An entry imported before its game was
    /// pulled here holds no game; once the game is here, the same file links it. That copy differs
    /// from this one only by the link this one lacks, so it is taken whatever the timestamps say:
    /// it adds a fact and overwrites nothing.
    /// </summary>
    public static MergeAction Entry(Versioned<EntryContent>? local, DateTime? localDeletedAtUtc, Versioned<EntryContent> incoming)
    {
        if (local is { } here && Links(here.Content, incoming.Content))
            return MergeAction.Update;

        return Row(local, localDeletedAtUtc, incoming);
    }

    /// <summary>
    /// The file's content as this database should compare it, when one of its references did not
    /// resolve here: a game or player this copy already knows is kept, not erased by a file that
    /// could not name it here.
    /// </summary>
    public static EntryContent KeepKnownLinks(EntryContent incoming, EntryContent? local, bool gameUnresolved, bool playerUnresolved)
        => local is null ? incoming : incoming with
        {
            GameId = gameUnresolved ? local.GameId : incoming.GameId,
            PlayerId = playerUnresolved ? local.PlayerId : incoming.PlayerId
        };

    /// <summary>Whether <paramref name="incoming"/> is <paramref name="local"/> with a game or player filled in that it lacked.</summary>
    public static bool Links(EntryContent local, EntryContent incoming)
    {
        var filled = local with
        {
            GameId = local.GameId ?? incoming.GameId,
            PlayerId = local.PlayerId ?? incoming.PlayerId
        };

        return filled != local && filled == incoming;
    }
}
