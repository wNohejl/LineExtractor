namespace XLines.Web.Components.Panels;

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
        // The feed kept the key when ESPN BET became theScore Bet.
        ["espnbet"] = "theScore Bet",
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

    /// <summary>
    /// A book's two-letter mark, for the Board's dense cells. Colour means state on this desk, so a
    /// book is told apart typographically (ADR 0008), which only works if no two books share a
    /// mark: three keys start "BE" and two "BO", and the feed's two keys for one book
    /// (<c>pointsbet</c>/<c>pointsbetus</c>, <c>caesars</c>/<c>williamhill_us</c>) must read alike. So
    /// every book this class names has its mark written here, and an unknown key falls back to
    /// its first two letters.
    /// </summary>
    public static string Code(string book)
    {
        if (string.IsNullOrWhiteSpace(book))
            return "—";

        var key = book.Trim();
        return Codes.TryGetValue(key, out var code)
            ? code
            : key.Length >= 2 ? key[..2].ToUpperInvariant() : key.ToUpperInvariant();
    }

    /// <summary>The keys this class names, for the tests that hold every one to a name and a distinct mark.</summary>
    public static IReadOnlyCollection<string> Known => Names.Keys;

    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ballybet"] = "BB",
        ["bet365"] = "B3",
        ["betfair_ex_us"] = "BF",
        ["betmgm"] = "MG",
        ["betonlineag"] = "BO",
        ["betrivers"] = "BR",
        ["betus"] = "BU",
        ["bovada"] = "BV",
        ["caesars"] = "CZ",
        ["circa"] = "CI",
        ["draftkings"] = "DK",
        ["espn"] = "ES",
        ["espnbet"] = "TS",
        // ESPN's own closing lines name the book as it was then, cased; "ES" would read as ESPN.
        ["ESPN BET"] = "EB",
        ["fanatics"] = "FN",
        ["fanduel"] = "FD",
        ["hardrockbet"] = "HR",
        ["lowvig"] = "LV",
        ["mybookieag"] = "MB",
        ["pinnacle"] = "PN",
        ["pointsbet"] = "PB",
        ["pointsbetus"] = "PB",
        ["superbook"] = "SB",
        ["unibet_us"] = "UB",
        ["williamhill_us"] = "CZ",
        ["wynnbet"] = "WB",
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
