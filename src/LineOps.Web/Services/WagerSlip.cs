using LineOps.Core.Analytics;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

public enum SlipMode { Straight, Parlay }

/// <summary>One wager banked on the slip: everything the wager window knew when it was added.</summary>
public sealed record SlipItem(
    Guid Id,
    int GameId, string Matchup, DateTimeOffset StartsAt, string SportKey,
    string Market, string Outcome, decimal? Line,
    string Book, int PriceAmerican, DateTimeOffset CapturedAt, bool IsClosing,
    decimal Stake, string? Note,
    FairPrice? Fair, double? Ev,
    bool ManualPrice = false)
{
    /// <summary>"ML · Chiefs -128".</summary>
    public string Selection => $"{SelectionText.Of(Market, Outcome, Line)} {OddsMath.FormatPrice(PriceAmerican)}";

    /// <summary>
    /// What the journal already held on this side when the wager was banked — "$25 at -128
    /// (draftkings), Sep 26 13:18 ET" — or null. Carried from the wager window's duplicate check
    /// rather than read per card: the window has just read it, and a slip of ten cards is not
    /// worth ten queries to restate what each window already said.
    /// </summary>
    public string? InJournal { get; init; }

    public EntryDraft ToDraft(decimal stake)
        => new(GameId, Market, null, Outcome, Line, PriceAmerican, stake, Book, Note, ManualPrice);
}

/// <summary>
/// The wagers an operator has banked this desk session, and the rules that govern them.
///
/// <para>
/// Circuit-scoped (registered <c>AddScoped</c>): one slip per desk, shared by every window on it,
/// gone with the circuit. Not persisted — a reload loses an unlogged slip, and a slip takes seconds
/// to rebuild (spec §11.1). No Blazor types: windows subscribe to <see cref="Changed"/>.
/// </para>
///
/// <para>
/// Being the only owner of the list is what enforces the rules: a closing price is never logged
/// (it stays so the operator sees why), a straight bet needs a stake, a parlay's legs are the
/// items the market still offers, and two items on one side are two bets — allowed, and said.
/// </para>
/// </summary>
public sealed class WagerSlip
{
    private readonly List<SlipItem> _items = [];

    public IReadOnlyList<SlipItem> Items => _items;
    public SlipMode Mode { get; private set; } = SlipMode.Straight;
    public decimal ParlayStake { get; private set; }
    public int? ParlayPriceQuoted { get; private set; }
    public string? ParlayBook { get; private set; }

    public event Action? Changed;

    /// <summary>Banks a wager. Returns it as stored, with the id the slip assigned.</summary>
    public SlipItem Add(SlipItem item)
    {
        var stored = item with { Id = Guid.NewGuid() };
        _items.Add(stored);
        Raise();
        return stored;
    }

    /// <summary>The one removal path. An id not on the slip is a no-op.</summary>
    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
            Raise();
    }

    public void Clear()
    {
        _items.Clear();
        ParlayStake = 0;
        ParlayPriceQuoted = null;
        ParlayBook = null;
        Raise();
    }

    /// <summary>
    /// Switches between straight bets and one parlay, keeping every item. A parlay is placed at
    /// one book, so switching to one proposes the book most of the legs came from.
    /// </summary>
    public void SetMode(SlipMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;

        if (mode == SlipMode.Parlay && ParlayBook is null)
            ParlayBook = Legs.GroupBy(l => l.Book).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        Raise();
    }

    /// <summary>The parlay's money: stake, the book's own price if it gave one, and the book.</summary>
    public void SetParlay(decimal stake, int? priceQuoted, string? book)
    {
        ParlayStake = stake;
        ParlayPriceQuoted = priceQuoted is 0 ? null : priceQuoted;
        ParlayBook = string.IsNullOrWhiteSpace(book) ? null : book;
        Raise();
    }

    /// <summary>Edits one item in place — its stake or note. The id is the slip's and survives the edit.</summary>
    public void Touch(Guid id, Func<SlipItem, SlipItem> edit)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index < 0)
            return;

        _items[index] = edit(_items[index]) with { Id = id };
        Raise();
    }

    /// <summary>
    /// Whether this item can be logged as the slip stands. Never a closing price: it is a number
    /// that could not have been taken from here. In straight mode, also a stake above zero; a
    /// parlay leg carries no stake of its own.
    /// </summary>
    public bool CanLog(SlipItem item)
        => Mode == SlipMode.Parlay ? IsLeg(item) : !item.IsClosing && item.Stake > 0;

    /// <summary>What Log would write in straight mode.</summary>
    public IReadOnlyList<SlipItem> Ready => _items.Where(CanLog).ToList();

    /// <summary>
    /// The items a parlay is built from: what the market still offers, one per game — the first
    /// on the slip for each. Two legs on one game are correlated, and the Bills to win beside the
    /// Bills to win is the same bet twice; a book will not price either as a parlay, and
    /// settlement grades legs as independent. The rest stay on the slip and say why.
    /// </summary>
    public IReadOnlyList<SlipItem> Legs
        => _items.Where(i => !i.IsClosing).GroupBy(i => i.GameId).Select(g => g.First()).ToList();

    public bool IsLeg(SlipItem item) => Legs.Any(l => l.Id == item.Id);

    /// <summary>A live item that is not a leg because an earlier item is on the same game.</summary>
    public bool SharesGameWithLeg(SlipItem item) => !item.IsClosing && !IsLeg(item);

    /// <summary>
    /// The parlay's price: the book's quote where it gave one, otherwise the legs' prices
    /// multiplied (<see cref="OddsMath.CombinedAmerican"/>). Null with fewer than two legs.
    /// </summary>
    public int? CombinedPrice
    {
        get
        {
            if (ParlayPriceQuoted is { } quoted)
                return quoted;

            var legs = Legs;
            return legs.Count >= 2 ? OddsMath.CombinedAmerican(legs.Select(l => l.PriceAmerican)) : null;
        }
    }

    /// <summary>Whether another item on the slip is on the same side of the same game.</summary>
    public bool HasTwin(SlipItem item)
        => _items.Any(o => o.Id != item.Id && o.GameId == item.GameId && o.Market == item.Market
                           && string.Equals(o.Outcome, item.Outcome, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<EntryDraft> StraightDrafts() => Ready.Select(i => i.ToDraft(i.Stake)).ToList();

    /// <summary>
    /// The parlay as the journal takes it. The quoted price only when the book gave one: without
    /// it settlement multiplies the legs, which is also how it reprices after a push.
    /// </summary>
    public ParlayDraft ToParlayDraft()
        => new(ParlayBook ?? string.Empty, ParlayStake, ParlayPriceQuoted, null, Legs.Select(l => l.ToDraft(0m)).ToList());

    /// <summary>Takes logged items off the slip, in one change. Whatever was not logged stays.</summary>
    public void Logged(IEnumerable<Guid> ids)
    {
        var gone = ids.ToHashSet();
        if (_items.RemoveAll(i => gone.Contains(i.Id)) > 0)
            Raise();
    }

    private void Raise() => Changed?.Invoke();
}
