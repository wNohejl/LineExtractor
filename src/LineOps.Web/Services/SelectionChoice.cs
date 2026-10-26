using LineOps.Core.Entities;
using LineOps.Data.CrossReference;

namespace LineOps.Web.Services;

/// <summary>
/// One side of one market on a board row: what a selection chip stands for. <see cref="Offer"/> is
/// null where no book prices the side — the side can still be chosen, and logged at a typed price.
/// </summary>
public sealed record SelectionChoice(string Market, string Outcome, BestOffer? Offer)
{
    public SelectionChoice(string market, BestOffer offer) : this(market, offer.Outcome, offer)
    {
    }

    public string Label => SelectionText.Of(Market, Outcome, Offer?.Line);

    /// <summary>
    /// Every side of the row, moneyline then spread then total, home/over first. A side no book
    /// prices is still listed, from the game's teams, so a bet taken where the desk has no number
    /// — a book it does not scan, a game before its lines were pulled — can be recorded.
    /// </summary>
    public static IReadOnlyList<SelectionChoice> All(BoardRow? row)
    {
        if (row is null)
            return [];

        var home = row.Game.HomeTeam?.Name;
        var away = row.Game.AwayTeam?.Name;
        var choices = new List<SelectionChoice>(6);

        foreach (var pair in new[] { row.Moneyline, row.Spread, row.Total })
        {
            var (first, second) = pair.Market == Markets.Total ? ("Over", "Under") : (home, away);

            Add(pair.Market, first, pair.First);
            Add(pair.Market, second, pair.Second);
        }

        return choices;

        void Add(string market, string? outcome, BestOffer? offer)
        {
            if (offer is not null)
                choices.Add(new SelectionChoice(market, offer));
            else if (outcome is not null)
                choices.Add(new SelectionChoice(market, outcome, null));
        }
    }

    /// <summary>
    /// Where a wager window opens. A door that names a side — a price pressed on the Board, a
    /// copy of a logged bet — lands on it, and on the named book's price where that book quotes
    /// the side. A door that names none lands on nothing: Place wager on a row opens on the
    /// question, every side offered and none chosen. It used to land on the side with the most
    /// value, so the window opened with a bet already made that the operator had not made, its
    /// step folded and the stake asked for next; a bet is the operator's to choose.
    /// <see cref="MostValue"/> is the ranking that used, kept for a hint.
    /// </summary>
    public static WagerOffer? Land(BoardRow? row, string? market, string? outcome, string? book)
    {
        if (market is null || outcome is null)
            return null;

        var asked = All(row).FirstOrDefault(c => c.Offer is not null && c.Market == market
                                                 && string.Equals(c.Outcome, outcome, StringComparison.OrdinalIgnoreCase));

        return asked is null
            ? null
            : (book is not null ? WagerOffer.AtBook(asked.Market, asked.Offer!, book) : null)
              ?? WagerOffer.Best(asked.Market, asked.Offer!);
    }

    /// <summary>
    /// The side with the most value against the fair price; where nothing can be scored, the
    /// widest spread between books — they disagree, which is the next best reason to look.
    /// A suggestion to name, never a choice to make for the operator.
    /// </summary>
    public static WagerOffer? MostValue(BoardRow? row)
        => All(row).Where(c => c.Offer is not null)
            .OrderByDescending(c => c.Offer!.Ev ?? double.MinValue)
            .ThenByDescending(c => c.Offer!.EdgePoints ?? 0)
            .Select(c => WagerOffer.Best(c.Market, c.Offer!))
            .FirstOrDefault();
}
