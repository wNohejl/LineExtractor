namespace LineOps.Web.Components.Panels;

/// <summary>
/// The name a book goes by, from the key the odds feed files it under.
///
/// The feed writes a bookmaker as a lowercase key — <c>draftkings</c>, <c>williamhill_us</c> —
/// and the windows that list books printed that key as the book's name. A key is for joining
/// rows; a person reads "DraftKings". The board's dense cells keep their two-letter monograms
/// (<c>PriceCell.Monogram</c>); anywhere a book is a row's label or named in a sentence, it goes
/// by this. A key with no entry here reads as its words capitalised, and one the feed already
/// cased ("DraftKings" from a provider that names its books) is left as written.
/// </summary>
public static class BookNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ballybet"] = "Bally Bet",
        ["bet365"] = "bet365",
        ["betfair_ex_us"] = "Betfair",
        ["betmgm"] = "BetMGM",
        ["betonlineag"] = "BetOnline.ag",
        ["betrivers"] = "BetRivers",
        ["betus"] = "BetUS",
        ["bovada"] = "Bovada",
        ["caesars"] = "Caesars",
        ["circa"] = "Circa",
        ["draftkings"] = "DraftKings",
        ["espn"] = "ESPN",
        ["espnbet"] = "ESPN BET",
        ["fanatics"] = "Fanatics",
        ["fanduel"] = "FanDuel",
        ["hardrockbet"] = "Hard Rock Bet",
        ["lowvig"] = "LowVig.ag",
        ["mybookieag"] = "MyBookie.ag",
        ["pinnacle"] = "Pinnacle",
        ["pointsbet"] = "PointsBet",
        ["pointsbetus"] = "PointsBet",
        ["superbook"] = "SuperBook",
        ["unibet_us"] = "Unibet",
        ["williamhill_us"] = "Caesars",
        ["wynnbet"] = "WynnBET",
    };

    /// <summary>"DraftKings" for <c>draftkings</c>; a key it does not know, in capitalised words.</summary>
    public static string Display(string? book)
    {
        if (string.IsNullOrWhiteSpace(book))
            return "—";

        if (Names.TryGetValue(book.Trim(), out var name))
            return name;

        // Already cased by whoever wrote it: their spelling is the book's own.
        if (book.Any(char.IsUpper))
            return book.Trim();

        return string.Join(' ', book.Trim()
            .Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
