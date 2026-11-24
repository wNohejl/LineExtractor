using System.Globalization;
using XLines.Core.Analytics;
using XLines.Data.CrossReference;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Services;

namespace XLines.Web.Components.Desk;

/// <summary>
/// A price typed by hand — the number the operator got, the line it was on and the book — and
/// the one reading of it into the <see cref="WagerOffer"/> a wager is logged against.
/// </summary>
/// <remarks>
/// Held by both places a price is typed: the Place wager window's Custom price… and the Board
/// take's. They drew the same three fields and read them the same way, so the reading lives here
/// once, with the fields beside it (<see cref="TypedPriceFields"/>): one validation, one offer,
/// and a take that logs exactly what the window would have logged for the same input. What a
/// field says is wrong is the Journal's own rule for that field (<see cref="JournalPanel.PriceError"/>,
/// <see cref="JournalPanel.LineError"/>), so a price refused here is refused there in the same words.
/// </remarks>
public sealed class TypedPrice
{
    /// <summary>What Log waits for while the typed price cannot be read.</summary>
    public const string NeedsPrice = "Needs a price (like -110 or +150) and a book.";

    /// <summary>What Log waits for while a spread or a total has no line it can be graded at.</summary>
    public const string NeedsLine = "Needs the line you got, like -3 or 47.5.";

    /// <summary>The book field's message once it has been cleared.</summary>
    public const string BookError = "Enter the book you took it at.";

    /// <summary>As typed: American to the cent (+104.35) or decimal odds (2.0435). Null while empty.</summary>
    public decimal? Price { get; private set; }

    public decimal? Line { get; private set; }

    /// <summary>The book field as it reads: a book's name ("DraftKings"), or whatever was typed.</summary>
    public string? Book { get; private set; }

    /// <summary>
    /// The book the fields were started from, as the feed keys it ("williamhill_us"). The field shows
    /// its name ("Caesars"), and while it still does, the key is what is recorded — exactly what
    /// was recorded when the field showed the key itself. Several keys share a name, so the name
    /// alone could not be turned back into the right one.
    /// </summary>
    private string? _startKey;

    // A field says what is wrong with it once it has been left, or once Log or Add to slip was
    // pressed — never at each keystroke on the way to a price (HIG: validate when the value is
    // entered; "-" on the way to "-115" is not an error).
    private bool _priceTouched;
    private bool _lineTouched;
    private bool _bookTouched;

    /// <summary>Whether anything has been typed into the fields since they were started.</summary>
    public bool Edited { get; private set; }

    /// <summary>
    /// Starts the fields from the number on screen, so changing -166 to -160 is one edit rather
    /// than three fields. With nothing on screen, what was typed last, else -110 and the book given.
    /// </summary>
    public void StartFrom(WagerOffer? from, string? book = null)
    {
        Price = from?.PriceAmerican ?? Price ?? -110;
        // "-3", not the "-3.00" the stored scale prints.
        Line = from?.Line is { } line ? line / 1.000000000000000000000000000000000m : Line;

        if (from?.Book is { } fetched)
            ShowBook(fetched);
        else if (Book is null && book is not null)
            ShowBook(book);

        Untouched();
    }

    /// <summary>Sets the fields as they were, untouched: a copied bet, or one put back by Same bet again.</summary>
    /// <param name="book">The book as recorded (a key, or the words typed then), shown as recorded.</param>
    public void Restore(decimal? price, decimal? line, string? book)
    {
        Price = price;
        Line = line;

        // As recorded: a copy shows the book as it was written in the journal.
        Book = book;
        _startKey = book;
        Untouched();
    }

    /// <summary>The next bet begins: the price and line go, the book stays as the likely next one.</summary>
    public void Clear()
    {
        Price = null;
        Line = null;
        Untouched();
    }

    // Values as they are typed (the fields are Immediate, so the headline, the "to win" figure
    // and the offer follow each keystroke); what is wrong is said once a field is left (Touch*).
    public void SetPrice(decimal? price)
    {
        Price = price;
        Edited = true;
    }

    public void SetLine(decimal? line)
    {
        Line = line;
        Edited = true;
    }

    public void SetBook(string? book)
    {
        Book = book;
        Edited = true;
    }

    public void TouchPrice() => _priceTouched = true;

    public void TouchLine() => _lineTouched = true;

    public void TouchBook() => _bookTouched = true;

    /// <summary>Log or Add to slip was pressed: every field says what is wrong with it.</summary>
    public void TouchAll() => _priceTouched = _lineTouched = _bookTouched = true;

    /// <summary>
    /// The book as it is recorded: the started book's own key while the field still names it,
    /// otherwise what was typed, trimmed and lower-cased — as the field has always recorded it.
    /// Null while the field is empty.
    /// </summary>
    public string? BookKey
        => string.IsNullOrWhiteSpace(Book) ? null
            : (_startKey is not null && string.Equals(Book.Trim(), BookNames.Display(_startKey), StringComparison.OrdinalIgnoreCase)
                ? _startKey
                : Book).Trim().ToLowerInvariant();

    private void ShowBook(string key)
    {
        _startKey = key;
        Book = BookNames.Display(key);
    }

    private void Untouched()
    {
        _priceTouched = _lineTouched = _bookTouched = false;
        Edited = false;
    }

    /// <summary>
    /// The longest American price a typed one may be, either way. No book prints +100000; past it
    /// a number is a slip of the keys, and the reading had no ceiling at all — a typed 1e27 passed
    /// as a price and the stake's return overflowed in the middle of a render.
    /// </summary>
    public const decimal Largest = 100_000m;

    /// <summary>The price field's message for a number past <see cref="Largest"/>.</summary>
    public const string TooLarge = "A price is no more than 100000 either way. Check for a digit too many.";

    /// <summary>
    /// The typed price as the American price it is read as (<see cref="OddsMath.ReadTypedPrice"/>):
    /// +104.35 as typed, and decimal odds 2.0435 as +104.35. Null while what is typed is not a
    /// price, or is past <see cref="Largest"/> — so Log waits on it as on any price it cannot read.
    /// </summary>
    public decimal? American
        => Price is { } typed && OddsMath.ReadTypedPrice(typed) is { } read && Math.Abs(read) <= Largest ? read : null;

    /// <summary>
    /// What a typed decimal price was read as — "Decimal 2.0435 = +104.35" — so the number logged
    /// is on screen before it is logged. Null for an American price, which is shown as typed.
    /// </summary>
    public string? DecimalEcho => EchoOf(Price);

    /// <summary>
    /// The one reading of a typed decimal price, wherever a price is typed — here, the Journal's
    /// edit form and the slip: "Decimal 2.0435 = +104.35". Null for an American price, or nothing typed.
    /// </summary>
    public static string? EchoOf(decimal? typed)
        => typed is { } t && Math.Abs(t) < 100 && OddsMath.ReadTypedPrice(t) is { } american
            ? $"Decimal {t.ToString("0.######", CultureInfo.InvariantCulture)} = {OddsMath.FormatPrice(american)}"
            : null;

    /// <summary>The price field's message, under it, once it has been typed in.</summary>
    public string? PriceError
        => !_priceTouched ? null
            : JournalPanel.PriceError(Price ?? 0) ?? (American is null ? TooLarge : null);

    /// <summary>The line field's message for this market, once it has been typed in.</summary>
    public string? LineError(string? market) => _lineTouched && market is not null ? JournalPanel.LineError(market, Line) : null;

    /// <summary>The book field's message, once it has been cleared.</summary>
    public string? BookFieldError => _bookTouched && string.IsNullOrWhiteSpace(Book) ? BookError : null;

    /// <summary>
    /// The price the fields stand for, or null while they do not stand for one: no readable price,
    /// no book, or — on a spread or a total — no line it could be graded at. Value is read against
    /// the side's fair price only on the fair price's own number (<see cref="WagerOffer.Typed"/>).
    /// </summary>
    /// <param name="hasLine">Whether the market takes a line (a spread or a total on a game).</param>
    /// <param name="side">The side's fetched offer, for its fair price; null where nothing is fetched.</param>
    /// <param name="described">A bet on no game, in the operator's words.</param>
    public WagerOffer? Offer(string? market, string? outcome, bool hasLine, BestOffer? side, DateTimeOffset now,
        string? described = null)
        => market is not null && outcome is not null
           && American is { } price && Math.Abs(price) >= 100
           && BookKey is { } book
           && !LineBlocks(market, hasLine)
            ? WagerOffer.Typed(market, outcome, hasLine ? Line : null, book, price, side, now, described)
            : null;

    /// <summary>
    /// The one place a typed price is refused for its line: a spread or a total with no number, or
    /// a total at zero or below, cannot be graded. Added with the take's typed price (the owner is
    /// being asked whether to keep it); return false here and the rule is gone from both forms.
    /// It never touches the Journal's own edit form, a duplicate, an import or a merge.
    /// </summary>
    private bool LineBlocks(string market, bool hasLine)
        => hasLine && JournalPanel.LineError(market, Line) is not null;

    /// <summary>What Log waits for while <see cref="Offer"/> is null: the line where that is what is missing.</summary>
    public string Waiting(string? market, bool hasLine)
        => market is not null && American is not null && BookKey is not null && LineBlocks(market, hasLine)
            ? NeedsLine
            : NeedsPrice;

    /// <summary>The side as typed, at the line typed — "Spread · Bills +3.5" — for the echo under the price.</summary>
    public string Echo(string market, string outcome, bool hasLine, decimal? fetchedLine)
        => SelectionText.Of(market, outcome, hasLine ? Line ?? fetchedLine : null);
}
