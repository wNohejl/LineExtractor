using XLines.Core.Analytics;
using XLines.Data.CrossReference;
using XLines.Web.Components.Desk;
using XLines.Web.Components.Panels;

namespace XLines.Web.Components.Snippets;

/// <summary>
/// Which books a market's table shows first, and how many before "View more".
///
/// <para>
/// The same game is read three ways — the Board row's More bets, the Game window's market opened
/// in place, and the Line movement window — and each listed every book, ten rows a market. Each
/// now shows the top <see cref="Top"/> and folds the rest behind a key that says how many
/// (<see cref="MoreBooks"/>). "Top" is one ordering everywhere, so the same five books lead in
/// all three: <b>the books holding the best price on either side first</b> (so the marked prices
/// are always among the five showing), <b>then the lowest hold</b> (the cheapest book to bet into),
/// a book quoting only one side (no hold) after those that quote both, and the book's name last,
/// so a tie reads the same way every time.
/// </para>
///
/// <para>
/// Only books with a price are ranked: a polled book that has not priced this market has no row,
/// and is not counted.
/// </para>
/// </summary>
public static class BookRanking
{
    /// <summary>How many books a market's table shows before "View more".</summary>
    public const int Top = 5;

    /// <summary>The order, in the words the tables' footnotes use.</summary>
    public const string OrderText = "books holding the best price on either side first, then the lowest hold";

    /// <summary>
    /// The books in ranked order: best on either side, then lowest hold, then one-sided books,
    /// then by name. Duplicates (a book on both sides) appear once.
    /// </summary>
    public static IReadOnlyList<string> Rank(IEnumerable<string> books, BestOffer? first, BestOffer? second)
        => books
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => IsBest(b, first) || IsBest(b, second) ? 0 : 1)
            .ThenBy(b => Hold(b, first, second) is null ? 1 : 0)
            .ThenBy(b => Hold(b, first, second) ?? 0)
            .ThenBy(b => BookNames.Display(b), StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every book priced on either side of the market, ranked.</summary>
    public static IReadOnlyList<string> Priced(BestOffer? first, BestOffer? second)
        => Rank((first?.Rungs ?? []).Select(r => r.Book).Concat((second?.Rungs ?? []).Select(r => r.Book)), first, second);

    /// <summary>
    /// Rows of any shape in ranked order, by the book each names: the Line movement window's
    /// rows and the Game window's moves are one side's, ranked on the whole market.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> rows, Func<T, string> book, BestOffer? first, BestOffer? second)
    {
        var list = rows.ToList();
        var rank = Rank(list.Select(book), first, second)
            .Select((b, i) => (b, i))
            .ToDictionary(x => x.b, x => x.i, StringComparer.OrdinalIgnoreCase);

        return list.OrderBy(r => rank.GetValueOrDefault(book(r), int.MaxValue)).ToList();
    }

    /// <summary>The rows a table shows: all of them expanded, otherwise the first <paramref name="top"/>.</summary>
    public static IReadOnlyList<T> Visible<T>(IReadOnlyList<T> ranked, bool expanded, int top = Top)
        => expanded || ranked.Count <= top ? ranked : ranked.Take(top).ToList();

    /// <summary>Whether the book holds the side's best price — the one price a table marks on it.</summary>
    public static bool IsBest(string book, BestOffer? side)
        => side is not null && string.Equals(side.Book, book, StringComparison.OrdinalIgnoreCase);

    /// <summary>The book's margin on the market, read from whichever side it quotes a pair on.</summary>
    public static double? Hold(string book, BestOffer? first, BestOffer? second)
        => Rung(book, first)?.Hold ?? Rung(book, second)?.Hold;

    /// <summary>The book's rung on a side, or null where it does not quote it.</summary>
    public static BookPrice? Rung(string book, BestOffer? side)
        => side?.Rungs.FirstOrDefault(r => string.Equals(r.Book, book, StringComparison.OrdinalIgnoreCase));

    /// <summary>"View 5 more books", or "View fewer books" once they show.</summary>
    public static string KeyLabel(int hidden, bool expanded)
        => expanded ? "View fewer books" : $"View {hidden} more {(hidden == 1 ? "book" : "books")}";

    /// <summary>"Top 5 of 10 books priced", "All 10 books priced".</summary>
    public static string CountText(int total, bool expanded, int top = Top)
        => expanded || total <= top
            ? $"All {total} {(total == 1 ? "book" : "books")} priced"
            : $"Top {top} of {total} books priced";

    /// <summary>
    /// A price as every book table writes it: the number where the market has one, then the price —
    /// "+3.5 -110", "o47.5 -105", "-128" (the Line movement window's <c>Quote</c>).
    /// </summary>
    public static string Quote(BookPrice rung, string outcome)
        => (rung.Line is { } line ? OddsMath.FormatLine(line, outcome) + " " : "") + OddsMath.FormatPrice(rung.PriceAmerican);

    /// <summary>
    /// The fair line under a market, spelled one way on every surface: "Fair Chiefs -136 · Broncos
    /// +115 (Pinnacle, no-vig)", or "(4-book consensus, no-vig)". Empty where neither side has one.
    /// </summary>
    public static string FairText(BestOffer? first, BestOffer? second)
    {
        var sides = new[] { first, second }
            .Where(o => o?.Fair is not null)
            .Select(o => $"{o!.Outcome}{(o.Line is { } l ? " " + OddsMath.FormatLine(l, o.Outcome) : "")} {OddsMath.FormatPrice(o.Fair!.PriceAmerican)}")
            .ToList();

        if (sides.Count == 0)
            return string.Empty;

        var basis = (first?.Fair ?? second?.Fair)!;
        return $"Fair {string.Join(" · ", sides)} ({Checks.Basis(basis)}, no-vig)";
    }
}
