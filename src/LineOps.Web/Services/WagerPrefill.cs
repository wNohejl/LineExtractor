using LineOps.Core.Entities;

namespace LineOps.Web.Services;

/// <summary>
/// A bet as a door hands it to the wager window: enough to open the window where the bet was,
/// so a copy starts where the original ended. Built from a journal entry (the Journal's
/// Duplicate) or from the offer on screen (the wager window's own), and read by the window as
/// its parameters. One shape for both doors, so "duplicate" means the same thing from either.
/// </summary>
/// <param name="GameId">The game, or null for a bet the desk does not track.</param>
/// <param name="Market"><c>Markets.*</c>, or "other" for a described bet.</param>
/// <param name="Outcome">The side; empty for a described bet.</param>
/// <param name="Line">The number taken, where the market has one.</param>
/// <param name="Book">The book the bet was at. The window opens on that book's price today.</param>
/// <param name="PriceAmerican">
/// Only for a price the operator typed: the copy types the same number. A fetched price is not
/// carried — a bet made again is made at today's price, not last week's.
/// </param>
/// <param name="Stake">The stake, so the copy is ready to log.</param>
/// <param name="Note">The note, carried as it was.</param>
/// <param name="Described">The free text of a bet on no market the desk prices.</param>
public sealed record WagerPrefill(
    int? GameId,
    string Market,
    string Outcome,
    decimal? Line,
    string Book,
    int? PriceAmerican,
    decimal Stake,
    string? Note,
    string? Described)
{
    public static WagerPrefill From(JournalEntry entry)
    {
        var described = string.IsNullOrWhiteSpace(entry.FreeTextMarket) ? null : entry.FreeTextMarket;
        // A typed price is the operator's number and travels; so does the price of a bet on no
        // game, which the desk could not fetch again.
        var typed = entry.ManualPrice || entry.GameId is null || described is not null;

        return new WagerPrefill(
            entry.GameId,
            described is null ? entry.Market : "other",
            described is null ? entry.Outcome : string.Empty,
            entry.LineTaken,
            entry.Book,
            typed ? entry.PriceTaken : null,
            entry.Stake,
            entry.Note,
            described);
    }

    public static WagerPrefill From(WagerOffer offer, int? gameId, decimal stake, string? note)
        => new(gameId, offer.Market, offer.Outcome, offer.Line, offer.Book,
            offer.Manual ? offer.PriceAmerican : null, stake,
            string.IsNullOrWhiteSpace(note) ? null : note, offer.Described);

    /// <summary>The wager window's parameters, by name. Nothing null goes in: an absent key is the window's default.</summary>
    public Dictionary<string, object> ToParameters()
    {
        var parameters = new Dictionary<string, object>();

        if (GameId is { } gameId) parameters["GameId"] = gameId;
        if (Described is null)
        {
            parameters["Market"] = Market;
            parameters["Outcome"] = Outcome;
        }
        if (Line is { } line) parameters["Line"] = line;
        if (!string.IsNullOrWhiteSpace(Book)) parameters["Book"] = Book;
        if (PriceAmerican is { } price) parameters["PriceAmerican"] = price;
        if (Stake > 0) parameters["Stake"] = Stake;
        if (Note is not null) parameters["Note"] = Note;
        if (Described is not null) parameters["Described"] = Described;

        return parameters;
    }
}
