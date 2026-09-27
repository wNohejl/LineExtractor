using LineOps.Data.CrossReference;

namespace LineOps.Web.Services;

/// <summary>One side of one market on a board row: what a selection chip stands for.</summary>
public sealed record SelectionChoice(string Market, BestOffer Offer)
{
    public string Label => SelectionText.Of(Market, Offer.Outcome, Offer.Line);

    /// <summary>Every priced side of the row, moneyline then spread then total, home/over first.</summary>
    public static IReadOnlyList<SelectionChoice> All(BoardRow? row)
    {
        if (row is null)
            return [];

        var choices = new List<SelectionChoice>(6);

        foreach (var pair in new[] { row.Moneyline, row.Spread, row.Total })
        {
            if (pair.First is { } first)
                choices.Add(new SelectionChoice(pair.Market, first));

            if (pair.Second is { } second)
                choices.Add(new SelectionChoice(pair.Market, second));
        }

        return choices;
    }

    /// <summary>
    /// Where a wager window opens. A door that names a side lands on it, and on the named book's
    /// price where that book quotes the side. Otherwise the side with the most value against the
    /// fair price; where nothing can be scored, the widest spread between books — they disagree,
    /// which is the next best reason the game is worth a look. A door that lands on the wrong side
    /// costs a click: every side is still on the strip.
    /// </summary>
    public static WagerOffer? Land(BoardRow? row, string? market, string? outcome, string? book)
    {
        var choices = All(row);

        if (market is not null && outcome is not null
            && choices.FirstOrDefault(c => c.Market == market
                                           && string.Equals(c.Offer.Outcome, outcome, StringComparison.OrdinalIgnoreCase)) is { } asked)
        {
            return (book is not null ? WagerOffer.AtBook(asked.Market, asked.Offer, book) : null)
                   ?? WagerOffer.Best(asked.Market, asked.Offer);
        }

        return choices
            .OrderByDescending(c => c.Offer.Ev ?? double.MinValue)
            .ThenByDescending(c => c.Offer.EdgePoints ?? 0)
            .Select(c => WagerOffer.Best(c.Market, c.Offer))
            .FirstOrDefault();
    }
}
