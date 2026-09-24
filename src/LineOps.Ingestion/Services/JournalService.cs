using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Ingestion.Services;

/// <summary>A straight bet or a parlay leg as the form holds it.</summary>
public sealed record EntryDraft(
    int? GameId,
    string Market,
    string? FreeTextMarket,
    string Outcome,
    decimal? LineTaken,
    int PriceTaken,
    decimal Stake,
    string Book,
    string? Note);

/// <summary>A parlay as the form holds it: the money on the parlay, the selections on the legs.</summary>
public sealed record ParlayDraft(string Book, decimal Stake, int? PriceQuoted, string? Note, IReadOnlyList<EntryDraft> Legs);

/// <summary>What the Journal is showing.</summary>
public sealed record JournalFilter(
    JournalStatus Status = JournalStatus.All,
    string? SportKey = null,
    string? Book = null,
    DateTimeOffset? Since = null,
    int Take = 100);

public enum JournalStatus { All, Pending, Settled }

/// <summary>What the Performance window is looking at.</summary>
public sealed record PerformanceFilter(
    string? SportKey = null,
    int? SeasonYear = null,
    string? Book = null,
    string? Market = null,
    DateTimeOffset? Since = null);

/// <summary>Bets for the money, selections for the close.</summary>
public sealed record PerformanceSet(IReadOnlyList<JournalEntry> Ledger, IReadOnlyList<JournalEntry> Selections);

/// <summary>
/// The journal's reads and writes: logging, editing and deleting entries and parlays, and the
/// lists the form picks from.
///
/// <para>
/// An edit to a graded entry is re-graded, not left standing: change the line on a spread that
/// was marked a win and the win may no longer be true. The entry goes back to pending with its
/// close cleared, and settlement grades it again from the game — the same path every other
/// entry takes, so an edited result can never disagree with the rule an unedited one follows.
/// A result the operator set by hand on a market nothing grades is theirs and is kept.
/// </para>
/// </summary>
public class JournalService(LineOpsDbContext db, SettlementService settlement)
{
    /// <summary>Books every desk knows, before any has been seen in the data.</summary>
    public static readonly IReadOnlyList<string> CommonBooks =
        ["betmgm", "bet365", "caesars", "draftkings", "espnbet", "fanatics", "fanduel", "pinnacle"];

    public async Task<IReadOnlyList<JournalEntry>> EntriesAsync(JournalFilter filter, CancellationToken ct = default)
    {
        var query = db.JournalEntries.AsNoTracking()
            .Include(e => e.Game).ThenInclude(g => g!.HomeTeam)
            .Include(e => e.Game).ThenInclude(g => g!.AwayTeam)
            .Include(e => e.Game).ThenInclude(g => g!.Sport)
            .Where(e => e.ParlayGroupId == null);

        query = filter.Status switch
        {
            JournalStatus.Pending => query.Where(e => e.Result == EntryResult.Pending),
            JournalStatus.Settled => query.Where(e => e.Result != EntryResult.Pending),
            _ => query
        };

        if (filter.SportKey is { Length: > 0 } sport)
            query = query.Where(e => e.Game != null && e.Game.Sport!.Key == sport);

        if (filter.Book is { Length: > 0 } book)
            query = query.Where(e => e.Book.ToLower() == book.ToLower());

        if (filter.Since is { } since)
            query = query.Where(e => e.PlacedAt >= since);

        return await query.OrderByDescending(e => e.PlacedAt).Take(filter.Take).ToListAsync(ct);
    }

    /// <summary>Parlays under the same filter, each with its legs and their games.</summary>
    public async Task<IReadOnlyList<Parlay>> ParlaysAsync(JournalFilter filter, CancellationToken ct = default)
    {
        var query = db.Parlays.AsNoTracking()
            .Include(p => p.Legs).ThenInclude(l => l.Game).ThenInclude(g => g!.HomeTeam)
            .Include(p => p.Legs).ThenInclude(l => l.Game).ThenInclude(g => g!.AwayTeam)
            .Include(p => p.Legs).ThenInclude(l => l.Game).ThenInclude(g => g!.Sport)
            .AsSplitQuery()
            .AsQueryable();

        query = filter.Status switch
        {
            JournalStatus.Pending => query.Where(p => p.Result == EntryResult.Pending),
            JournalStatus.Settled => query.Where(p => p.Result != EntryResult.Pending),
            _ => query
        };

        if (filter.SportKey is { Length: > 0 } sport)
            query = query.Where(p => p.Legs.Any(l => l.Game != null && l.Game.Sport!.Key == sport));

        if (filter.Book is { Length: > 0 } book)
            query = query.Where(p => p.Book.ToLower() == book.ToLower());

        if (filter.Since is { } since)
            query = query.Where(p => p.PlacedAt >= since);

        return await query.OrderByDescending(p => p.PlacedAt).Take(filter.Take).ToListAsync(ct);
    }

    /// <summary>
    /// What Performance reads, under its filters: the ledger — every bet once, parlays as one row
    /// (see <see cref="ParlayGrading.Ledger"/>) — and the selections CLV is measured on, which are
    /// straight bets and parlay legs alike, since a leg is priced against the close like any bet.
    ///
    /// <para>
    /// Filtered in memory after one load. A journal is hundreds of rows, not millions, and the
    /// filters cut across straights and parlays in ways one query per shape would repeat.
    /// </para>
    /// </summary>
    public async Task<PerformanceSet> PerformanceAsync(PerformanceFilter filter, CancellationToken ct = default)
    {
        var entries = await db.JournalEntries.AsNoTracking()
            .Include(e => e.Game).ThenInclude(g => g!.Sport)
            .ToListAsync(ct);

        var parlays = await db.Parlays.AsNoTracking()
            .Include(p => p.Legs).ThenInclude(l => l.Game).ThenInclude(g => g!.Sport)
            .ToListAsync(ct);

        bool Keep(JournalEntry e)
            => (filter.SportKey is not { Length: > 0 } sport || e.Game?.Sport?.Key == sport)
               && (filter.SeasonYear is not { } season || e.Game?.SeasonYear == season)
               && (filter.Book is not { Length: > 0 } book || string.Equals(e.Book, book, StringComparison.OrdinalIgnoreCase))
               && (filter.Market is not { Length: > 0 } market || e.Market == market)
               && (filter.Since is not { } since || (e.SettledAt ?? e.PlacedAt) >= since);

        var ledger = ParlayGrading.Ledger(entries, parlays).Where(Keep).ToList();
        var selections = entries.Where(e => e.IsGraded).Where(Keep).ToList();

        return new PerformanceSet(ledger, selections);
    }

    /// <summary>The bankroll the curve starts from. Zero until the operator says otherwise.</summary>
    public async Task<decimal> StartingBankrollAsync(CancellationToken ct = default)
    {
        var setting = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.StartingBankroll, ct);

        return decimal.TryParse(setting?.Value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
    }

    public async Task SetStartingBankrollAsync(decimal amount, CancellationToken ct = default)
    {
        var value = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.StartingBankroll, ct);

        if (setting is null)
            db.AppSettings.Add(new AppSetting { Key = AppSetting.StartingBankroll, Value = value });
        else
            setting.Value = value;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every entry on one game, straight or a leg — what the Game window lists.</summary>
    public async Task<IReadOnlyList<JournalEntry>> OnGameAsync(int gameId, CancellationToken ct = default)
        => await db.JournalEntries.AsNoTracking()
            .Include(e => e.Parlay)
            .Where(e => e.GameId == gameId)
            .OrderByDescending(e => e.PlacedAt)
            .ToListAsync(ct);

    /// <summary>Logs a straight bet, or edits one when <paramref name="id"/> is given.</summary>
    public async Task<JournalEntry> SaveEntryAsync(EntryDraft draft, int? id = null, CancellationToken ct = default)
    {
        JournalEntry entry;

        if (id is { } existingId)
        {
            entry = await db.JournalEntries.FirstAsync(e => e.Id == existingId, ct);

            if (entry.IsParlayLeg)
                throw new InvalidOperationException("A parlay leg is edited with its parlay.");
        }
        else
        {
            entry = new JournalEntry { PlacedAt = DateTimeOffset.UtcNow, Result = EntryResult.Pending };
            db.JournalEntries.Add(entry);
        }

        // Apply first, always: a new entry needs the draft written as much as an edit does.
        var changed = Apply(entry, draft);
        var regrade = id is not null && changed;

        await db.SaveChangesAsync(ct);

        if (regrade)
            await settlement.SettleAsync(ct);

        return entry;
    }

    /// <summary>Logs a parlay with its legs. Each leg is a selection with no stake of its own.</summary>
    public async Task<Parlay> SaveParlayAsync(ParlayDraft draft, CancellationToken ct = default)
    {
        if (draft.Legs.Count < 2)
            throw new InvalidOperationException("A parlay has at least two legs.");

        var placed = DateTimeOffset.UtcNow;
        var parlay = new Parlay
        {
            Book = Normalise(draft.Book),
            Stake = draft.Stake,
            PriceQuoted = draft.PriceQuoted is 0 ? null : draft.PriceQuoted,
            Note = Blank(draft.Note),
            PlacedAt = placed
        };

        foreach (var leg in draft.Legs)
        {
            var entry = new JournalEntry { PlacedAt = placed, Result = EntryResult.Pending };
            Apply(entry, leg with { Stake = 0m, Book = draft.Book, Note = null });
            parlay.Legs.Add(entry);
        }

        db.Parlays.Add(parlay);
        await db.SaveChangesAsync(ct);
        return parlay;
    }

    /// <summary>Deletes a straight entry. A leg goes with its parlay, not on its own.</summary>
    public async Task DeleteEntryAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.JournalEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return;

        if (entry.IsParlayLeg)
            throw new InvalidOperationException("A parlay leg is deleted with its parlay.");

        db.JournalEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteParlayAsync(Guid id, CancellationToken ct = default)
    {
        var parlay = await db.Parlays.Include(p => p.Legs).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (parlay is null)
            return;

        db.Parlays.Remove(parlay);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets a parlay's result by hand — for the one a missing price left pending.</summary>
    public async Task SetParlayResultAsync(Guid id, EntryResult result, decimal? payout, CancellationToken ct = default)
    {
        var parlay = await db.Parlays.FirstAsync(p => p.Id == id, ct);

        ParlayGrading.Apply(parlay, new ParlayOutcome(result, result switch
        {
            EntryResult.Win => payout,
            EntryResult.Loss => 0m,
            EntryResult.Push or EntryResult.Void => parlay.Stake,
            _ => null
        }));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The books to pick from: the common ones, and any the data has seen. A picker rather than a
    /// text box, because "DraftKings", "draftkings" and "DK" are one book to a person and three to
    /// every breakdown and close lookup.
    /// </summary>
    public async Task<IReadOnlyList<string>> KnownBooksAsync(CancellationToken ct = default)
    {
        var seen = await db.ClosingLines.Select(c => c.Book.ToLower())
            .Union(db.JournalEntries.Select(e => e.Book.ToLower()))
            .Union(db.Parlays.Select(p => p.Book.ToLower()))
            .Distinct()
            .ToListAsync(ct);

        return seen.Concat(CommonBooks)
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Games to log a bet against, found by team: "mets", or "mets braves" for the matchup. Across
    /// the enabled sports and a season's reach rather than the last seven days, nearest to now
    /// first — the game being bet on is usually today's, and the one being logged late is
    /// usually last week's.
    /// </summary>
    public async Task<IReadOnlyList<Game>> SearchGamesAsync(string text, int take = 20, CancellationToken ct = default)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return [];

        var now = DateTimeOffset.UtcNow;
        var query = db.Games.AsNoTracking()
            .Include(g => g.HomeTeam).Include(g => g.AwayTeam).Include(g => g.Sport)
            .Where(g => g.Sport!.Enabled
                        && g.StartsAt >= now.AddDays(-200)
                        && g.StartsAt <= now.AddDays(30));

        foreach (var word in words)
        {
            var pattern = $"%{EscapeLike(word)}%";
            query = query.Where(g =>
                EF.Functions.ILike(g.HomeTeam!.Name, pattern, @"\")
                || EF.Functions.ILike(g.AwayTeam!.Name, pattern, @"\")
                || EF.Functions.ILike(g.HomeTeam!.Abbrev, pattern, @"\")
                || EF.Functions.ILike(g.AwayTeam!.Abbrev, pattern, @"\"));
        }

        // A team's whole reach is under two hundred games, so the nearest-first order is taken
        // in memory rather than asked of the database as an interval expression.
        var found = await query.OrderByDescending(g => g.StartsAt).Take(200).ToListAsync(ct);

        return found.OrderBy(g => (g.StartsAt - now).Duration()).Take(take).ToList();
    }

    /// <summary>
    /// Writes the draft onto the entry. Returns true when a field the grade depends on changed on
    /// an entry settlement can grade — which sends it back to pending to be graded again.
    /// </summary>
    private static bool Apply(JournalEntry entry, EntryDraft draft)
    {
        var free = !string.IsNullOrWhiteSpace(draft.FreeTextMarket);
        var market = free ? "other" : draft.Market;
        var outcome = string.IsNullOrWhiteSpace(draft.Outcome) ? "—" : draft.Outcome.Trim();

        var gradeChanged = entry.GameId != draft.GameId
                           || entry.Market != market
                           || entry.Outcome != outcome
                           || entry.LineTaken != draft.LineTaken
                           || entry.PriceTaken != draft.PriceTaken
                           || entry.Stake != draft.Stake;

        entry.GameId = draft.GameId;
        entry.Market = market;
        entry.FreeTextMarket = free ? draft.FreeTextMarket!.Trim() : null;
        entry.Outcome = outcome;
        entry.LineTaken = draft.LineTaken;
        entry.PriceTaken = draft.PriceTaken;
        entry.Stake = draft.Stake;
        entry.Note = Blank(draft.Note);

        var book = Normalise(draft.Book);
        var bookChanged = entry.Book != book;
        entry.Book = book;

        var gradable = !free && entry.GameId is not null;

        if (entry.Result == EntryResult.Pending || !gradable || !(gradeChanged || bookChanged))
            return false;

        // Back to pending, close and all: the close was matched on the old book, market and
        // outcome, so it may belong to a different bet now.
        PerformanceAnalytics.ApplyResult(entry, EntryResult.Pending);
        entry.ClosingSnapshotId = null;
        entry.ClosingCapturedAt = null;
        entry.ClosingPrice = null;
        entry.ClosingPoints = null;
        entry.ClosingBook = null;
        return true;
    }

    private static string Normalise(string book) => book.Trim().ToLowerInvariant();

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A typed % or _ is a character to find, not a wildcard.</summary>
    private static string EscapeLike(string text)
        => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
