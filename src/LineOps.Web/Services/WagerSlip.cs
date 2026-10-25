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

    /// <summary>
    /// Banked as a parlay leg — from a Board price, or a wager window building a parlay — and so
    /// carrying no stake: a leg's money is the parlay's. It is logged only inside a parlay; typing
    /// a stake on the card makes it an ordinary wager that can also be logged straight.
    /// </summary>
    public bool AsLeg { get; init; }

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

    /// <summary>
    /// Banks a wager. Returns it as stored, with the id the slip assigned. A stake at or below
    /// zero is refused outright: a $0 bet is not a bet, and a card that could be banked unstaked
    /// was a card the slip then had to explain. A leg is the exception, and has its own door
    /// (<see cref="AddLeg"/>): it is not a bet on its own.
    /// </summary>
    public SlipItem Add(SlipItem item)
    {
        if (item.AsLeg)
            return AddLeg(item);

        if (item.Stake <= 0)
            throw new ArgumentOutOfRangeException(nameof(item), "A wager on the slip needs a stake above zero.");

        var stored = item with { Id = Guid.NewGuid() };
        _items.Add(stored);
        Raise();
        return stored;
    }

    /// <summary>
    /// Banks a parlay leg: no stake, logged only inside a parlay. A slip holding nothing but legs
    /// is a parlay being built, so it switches to parlay mode; one with straight bets on it keeps
    /// the mode the operator chose.
    /// </summary>
    public SlipItem AddLeg(SlipItem item)
    {
        var stored = item with { Id = Guid.NewGuid(), Stake = 0m, AsLeg = true };
        _items.Add(stored);

        if (_items.All(i => i.AsLeg))
            SetModeQuietly(SlipMode.Parlay);

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
        if (SetModeQuietly(mode))
            Raise();
    }

    private bool SetModeQuietly(SlipMode mode)
    {
        if (Mode == mode)
            return false;

        Mode = mode;

        if (mode == SlipMode.Parlay && ParlayBook is null)
            ParlayBook = Legs.GroupBy(l => l.Book).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        return true;
    }

    /// <summary>The parlay's money: stake, the book's own price if it gave one, and the book.</summary>
    public void SetParlay(decimal stake, int? priceQuoted, string? book)
    {
        ParlayStake = stake;
        ParlayPriceQuoted = priceQuoted is 0 ? null : priceQuoted;
        ParlayBook = string.IsNullOrWhiteSpace(book) ? null : book;
        Raise();
    }

    /// <summary>
    /// Edits one item in place — its stake or note. The id is the slip's and survives the edit.
    /// An edit that would leave the stake at or below zero is refused and the card keeps its
    /// stake: the same rule as <see cref="Add"/>, so the slip never holds a $0 bet. A leg stays
    /// a leg at no stake; given one, it is a wager like any other.
    /// </summary>
    public void Touch(Guid id, Func<SlipItem, SlipItem> edit)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index < 0)
            return;

        var edited = edit(_items[index]) with { Id = id };

        if (edited.Stake > 0)
            edited = edited with { AsLeg = false };
        else if (!edited.AsLeg)
            return;

        _items[index] = edited;
        Raise();
    }

    /// <summary>
    /// Whether this item can be logged as the slip stands. Never a closing price: it is a number
    /// that could not have been taken from here. In straight mode, also a stake above zero — the
    /// slip never admits one at zero, so this is the invariant restated, not a state a card can
    /// be in; a parlay leg carries no stake of its own, and a card banked as one is logged only
    /// inside a parlay.
    /// </summary>
    public bool CanLog(SlipItem item)
        => Mode == SlipMode.Parlay ? IsLeg(item) : !item.IsClosing && !item.AsLeg && item.Stake > 0;

    /// <summary>What Log would write in straight mode.</summary>
    public IReadOnlyList<SlipItem> Ready => _items.Where(CanLog).ToList();

    /// <summary>
    /// The items a parlay is built from: what the market still offers, one per market on a game —
    /// the first on the slip for each. A side and a total on one game is a same-game parlay, which
    /// books write; the Bills to win beside the Bills to win is the same bet twice, and the over
    /// beside the under cannot both win, so a second item on a market a leg already holds is not
    /// a leg. The rest stay on the slip and say why.
    /// </summary>
    public IReadOnlyList<SlipItem> Legs
        => _items.Where(i => !i.IsClosing).GroupBy(i => (i.GameId, i.Market)).Select(g => g.First()).ToList();

    public bool IsLeg(SlipItem item) => Legs.Any(l => l.Id == item.Id);

    /// <summary>A live item that is not a leg because an earlier item is on the same market of the same game.</summary>
    public bool SharesMarketWithLeg(SlipItem item) => !item.IsClosing && !IsLeg(item);

    /// <summary>
    /// Two or more legs on one game. Their results move together — a favourite covering and the
    /// game going over are not independent — so the legs' prices multiplied overstate what the
    /// parlay pays, and only the book's own price for it is a price at all.
    /// </summary>
    public bool IsSameGame => Legs.GroupBy(l => l.GameId).Any(g => g.Count() > 1);

    /// <summary>The legs' prices multiplied, whatever the book quoted. Null with fewer than two legs.</summary>
    public int? MultipliedPrice
    {
        get
        {
            var legs = Legs;
            return legs.Count >= 2 ? OddsMath.CombinedAmerican(legs.Select(l => l.PriceAmerican)) : null;
        }
    }

    /// <summary>
    /// The parlay's price: the book's quote where it gave one, otherwise the legs' prices
    /// multiplied (<see cref="OddsMath.CombinedAmerican"/>). Null with fewer than two legs, and
    /// null for a same-game parlay the book's price has not been given for (<see cref="IsSameGame"/>).
    /// </summary>
    public int? CombinedPrice
        => ParlayPriceQuoted is { } quoted ? quoted
            : IsSameGame ? null
            : MultipliedPrice;

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
