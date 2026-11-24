using System.Globalization;

namespace XLines.Web.Components.Desk;

/// <summary>
/// A take priced by hand in its row, handed to the full wager form (the take's Full wager form
/// key): the typed book, the stake chosen so far, and the typed price and line, which the form's
/// typed price starts from. The price is the American price it was read as, or — typed only in
/// part — the number as typed, which the form's own fields read and say what is wrong with.
/// </summary>
public sealed record TypedTake(string Book, decimal Stake, decimal PriceAmerican, decimal? Line)
{
    /// <summary>
    /// The Place wager window's parameters for a take's Full wager form: the side and book, the
    /// stake typed so far, and — from a typed take — the price and line, which open the form with
    /// its price typed. Invariant text, as WagerPrefill writes it: a window's parameters
    /// round-trip as JSON and a decimal cannot be set from what a saved desk restores.
    /// </summary>
    public static Dictionary<string, object> Door(int gameId, string market, string outcome, string book,
        decimal stake, TypedTake? typed = null)
    {
        var parameters = new Dictionary<string, object>
        {
            ["GameId"] = gameId,
            ["Market"] = market,
            ["Outcome"] = outcome,
            ["Book"] = book
        };

        if (stake > 0)
            parameters["Stake"] = stake.ToString(CultureInfo.InvariantCulture);

        if (typed is not null)
        {
            parameters["PriceAmerican"] = typed.PriceAmerican.ToString("G29", CultureInfo.InvariantCulture);
            if (typed.Line is { } line)
                parameters["TakenLine"] = line.ToString("G29", CultureInfo.InvariantCulture);
        }

        return parameters;
    }
}
