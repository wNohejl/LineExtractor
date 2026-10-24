using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// The one price a wager is logged against: a side's best offer, one book's rung of it, or a
/// price the operator typed. Everything the checks read and the journal writes comes from here, so
/// the number on screen and the number logged cannot differ.
/// </summary>
public sealed record WagerOffer(
    string Market,
    string Outcome,
    decimal? Line,
    string Book,
    int PriceAmerican,
    DateTimeOffset CapturedAt,
    bool IsClosing,
    FairPrice? Fair,
    double? Ev,
    int BooksQuoting,
    bool IsBest)
{
    /// <summary>
    /// The operator typed this price — the number they got, perhaps hours ago or at a book the desk
    /// does not scan. Never closing: a typed price is a claim about what was taken, not the close.
    /// </summary>
    public bool Manual { get; init; }

    /// <summary>
    /// The market's free-text description, for a bet with no game or no market the desk prices —
    /// "Steelers to win the AFC North". Only ever on a typed price.
    /// </summary>
    public string? Described { get; init; }

    public string Label => Described ?? SelectionText.Of(Market, Outcome, Line);

    public static WagerOffer Best(string market, BestOffer offer)
        => new(market, offer.Outcome, offer.Line, offer.Book, offer.PriceAmerican, offer.CapturedAt,
            offer.IsClosing, offer.Fair, offer.Ev, offer.Rungs.Count, IsBest: true);

    /// <summary>
    /// One book's price on this side, or null when that book does not quote it. Its own number,
    /// capture time and value; the side's fair price only when the book is on the same number.
    /// </summary>
    public static WagerOffer? AtBook(string market, BestOffer offer, string book)
    {
        var rung = offer.Rungs.FirstOrDefault(r => string.Equals(r.Book, book, StringComparison.OrdinalIgnoreCase));
        return rung is null ? null : AtRung(market, offer, rung);
    }

    /// <summary>Every book quoting the side, best price first — the list the Price section picks from.</summary>
    public static IReadOnlyList<WagerOffer> Books(string market, BestOffer offer)
        => offer.Rungs.Select(r => AtRung(market, offer, r)).OrderByDescending(o => o.IsBest).ToList();

    /// <summary>
    /// A price the operator typed. Value is read against the side's fair price only where the typed
    /// number is the fair price's own; a half point off it, value is unread, as for any book.
    /// </summary>
    public static WagerOffer Typed(string market, string outcome, decimal? line, string book, int priceAmerican,
        BestOffer? side, DateTimeOffset now, string? described = null)
    {
        var fair = side?.Fair is { } f && side.Line == line ? f : null;
        var ev = fair is not null && priceAmerican != 0 ? FairValue.Ev(fair.Probability, priceAmerican) : (double?)null;

        return new WagerOffer(market, outcome, line, book, priceAmerican, now, IsClosing: false, fair, ev,
            side?.Rungs.Count ?? 0, IsBest: false)
        {
            Manual = true,
            Described = described
        };
    }

    private static WagerOffer AtRung(string market, BestOffer offer, BookPrice rung)
        => new(market, offer.Outcome, rung.Line, rung.Book, rung.PriceAmerican,
            rung.CapturedAt ?? offer.CapturedAt, offer.IsClosing,
            rung.Line == offer.Line ? offer.Fair : null, rung.Ev, offer.Rungs.Count,
            IsBest: string.Equals(rung.Book, offer.Book, StringComparison.OrdinalIgnoreCase));

    public EntryDraft ToDraft(int? gameId, decimal stake, string? note)
        => new(gameId, Described is null ? Market : "other", Described, Described is null ? Outcome : string.Empty,
            Line, PriceAmerican, stake, Book, string.IsNullOrWhiteSpace(note) ? null : note, Manual);

    public SlipItem ToSlipItem(Game game, string matchup, decimal stake, string? note)
        => new(Guid.Empty, game.Id, matchup, game.StartsAt, game.Sport?.Key ?? string.Empty,
            Market, Outcome, Line, Book, PriceAmerican, CapturedAt, IsClosing, stake,
            string.IsNullOrWhiteSpace(note) ? null : note, Fair, Ev, Manual);
}
