using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// The one price a wager is logged against: a side's best offer, or one book's rung of it when
/// a door named the book. Everything the checks read and the journal writes comes from here, so
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
    public string Label => SelectionText.Of(Market, Outcome, Line);

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
        if (rung is null)
            return null;

        return new WagerOffer(market, offer.Outcome, rung.Line, rung.Book, rung.PriceAmerican,
            rung.CapturedAt ?? offer.CapturedAt, offer.IsClosing,
            rung.Line == offer.Line ? offer.Fair : null, rung.Ev, offer.Rungs.Count,
            IsBest: string.Equals(rung.Book, offer.Book, StringComparison.OrdinalIgnoreCase));
    }

    public EntryDraft ToDraft(int gameId, decimal stake, string? note)
        => new(gameId, Market, null, Outcome, Line, PriceAmerican, stake, Book,
            string.IsNullOrWhiteSpace(note) ? null : note);

    public SlipItem ToSlipItem(Game game, string matchup, decimal stake, string? note)
        => new(Guid.Empty, game.Id, matchup, game.StartsAt, game.Sport?.Key ?? string.Empty,
            Market, Outcome, Line, Book, PriceAmerican, CapturedAt, IsClosing, stake,
            string.IsNullOrWhiteSpace(note) ? null : note, Fair, Ev);
}
