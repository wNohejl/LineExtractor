using System.Globalization;
using XLines.Core.Analytics;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Desk;
using XLines.Web.Components.Panels;

namespace XLines.Web.Services;

/// <summary>One side's price on one market as the Form answer reads it: the best now, where, and where that book opened.</summary>
/// <param name="Quote">"+2.5 -105", "o47.5 -110", "-128".</param>
/// <param name="Opened">"opened +3 -110", "unmoved", or null where the book's first reading is not on file.</param>
/// <param name="Edge">The edge against fair at the best book's number ("+2.7%"), or null where it has none.</param>
public sealed record TapeQuote(string Quote, string Book, string? Opened, string? Edge);

/// <summary>One market across the two sides, the away (or the over) leading.</summary>
/// <param name="Detail">What the market as a whole reads: its fair price and the lowest hold, or "closing line".</param>
public sealed record TapeMarket(string Name, TapeQuote? Leading, TapeQuote? Trailing, string? Detail);

/// <summary>One club's season, as the Form answer compares it with the other's.</summary>
public sealed record TapeClub(
    int Played,
    string Record,
    string HomeAway,
    string PerGame,
    string Ats,
    string OverUnder,
    string Rest,
    IReadOnlyList<TeamGameLogRow> Season,
    string? LastGame,
    string? Starter);

/// <summary>Everything the Form answer draws for a team sport, read once for the opened row.</summary>
public sealed record MatchupTapeData(BoardRow Row, IReadOnlyList<TapeMarket> Markets, TapeClub Away, TapeClub Home);

/// <summary>
/// The Form answer's arithmetic for a team sport, free of the database so each figure is tested
/// against the rows it came from. Nothing is estimated: a market no book priced reads "no line",
/// a season with no finals reads as none, and a record with no closing line behind it is not a
/// record of zero.
/// </summary>
public static class MatchupTape
{
    /// <summary>The three markets, away (or over) leading as the matchup reads.</summary>
    public static IReadOnlyList<TapeMarket> Markets(BoardRow row, IReadOnlyList<OddsSnapshot> snapshots)
        =>
        [
            Market("Moneyline", row.Moneyline, row, snapshots, overLeads: false),
            Market("Spread", row.Spread, row, snapshots, overLeads: false),
            Market("Total", row.Total, row, snapshots, overLeads: true)
        ];

    private static TapeMarket Market(string name, MarketPair pair, BoardRow row, IReadOnlyList<OddsSnapshot> snapshots, bool overLeads)
    {
        // The board lists home (or over) first; the answer reads away first, as "Bills at Chiefs"
        // does, and the over first, as a total is said.
        var (leading, trailing) = overLeads ? (pair.First, pair.Second) : (pair.Second, pair.First);

        string? detail = null;

        if (pair.IsPriced)
        {
            if (row.PricesAreClosing || (pair.First ?? pair.Second)!.IsClosing)
            {
                detail = "closing line";
            }
            else
            {
                var parts = new List<string>(2);

                if (new[] { pair.First, pair.Second }.FirstOrDefault(o => o?.Fair is not null) is { Fair: { } fair } fairSide)
                    parts.Add($"fair {SelectionText.Short(fairSide.Outcome)} {Figure(fairSide.Line, fairSide.Outcome, fair.PriceAmerican)}");

                var holds = new[] { pair.First, pair.Second }
                    .Where(o => o is not null)
                    .SelectMany(o => o!.Rungs)
                    .Where(r => r.Hold is not null)
                    .ToList();

                if (holds.Count > 0)
                {
                    var low = holds.MinBy(r => r.Hold!.Value)!;
                    parts.Add($"lowest hold {(low.Hold!.Value * 100).ToString("0.0", CultureInfo.InvariantCulture)}% ({BookNames.Display(low.Book)})");
                }

                detail = parts.Count == 0 ? null : string.Join(" · ", parts);
            }
        }

        return new TapeMarket(name, Quote(pair.Market, leading, snapshots), Quote(pair.Market, trailing, snapshots), detail);
    }

    private static TapeQuote? Quote(string market, BestOffer? offer, IReadOnlyList<OddsSnapshot> snapshots)
    {
        if (offer is null)
            return null;

        // Where the best book opened on this side: its first reading on file, on this market.
        var opened = snapshots
            .Where(s => s.Market == market
                        && string.Equals(s.Outcome, offer.Outcome, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(s.Book, offer.Book, StringComparison.OrdinalIgnoreCase))
            .MinBy(s => s.CapturedAt);

        var openedText = opened is null ? null
            : opened.PriceAmerican == offer.PriceAmerican && opened.Line == offer.Line ? "unmoved"
            : $"opened {OddsPanel.Quote(opened)}";

        var edge = offer.Fair is not null && offer.Ev is { } ev && !offer.IsClosing ? Checks.Percent(ev) : null;

        return new TapeQuote(Figure(offer.Line, offer.Outcome, offer.PriceAmerican), BookNames.Display(offer.Book), openedText, edge);
    }

    /// <summary>"+2.5 -105", "o47.5 -110", "-128": the line is part of the price.</summary>
    public static string Figure(decimal? line, string outcome, int price)
        => (line is { } l ? OddsMath.FormatLine(l, outcome) + " " : "") + OddsMath.FormatPrice(price);

    /// <summary>
    /// One club's season — its games of this game's season and season type, newest first — read
    /// as the answer compares it: record and where it was won, what it scores and allows a game,
    /// against the spread and the total where a closing line was captured, rest before this game,
    /// the last game played, and the starter the board names.
    /// </summary>
    public static TapeClub Club(IReadOnlyList<TeamGameLogRow> season, DateTimeOffset startsAt,
        (string Role, string Name, string Title)? starter)
    {
        var finals = season.Where(r => r.Status == GameStatus.Final && r.StartsAt < startsAt).ToList();
        var scored = finals.Where(r => r.ScoreFor is not null && r.ScoreAgainst is not null).ToList();

        var perGame = scored.Count == 0
            ? "—"
            : $"{Average(scored.Select(r => r.ScoreFor!.Value))} – {Average(scored.Select(r => r.ScoreAgainst!.Value))}";

        var (covers, fails, pushes) = GameLogSplits.AtsRecord(finals);
        var (overs, unders, totalPushes) = GameLogSplits.TotalRecord(finals);

        var last = finals.MaxBy(r => r.StartsAt);
        var rest = last is null
            ? "first game this season"
            : Days(LeagueClock.DateOf(startsAt).DayNumber - LeagueClock.DateOf(last.StartsAt).DayNumber);

        return new TapeClub(
            finals.Count,
            finals.Count == 0 ? "—" : GameLogSplits.FormatRecord(finals),
            finals.Count == 0 ? "—"
                : $"{GameLogSplits.FormatRecord(GameLogSplits.Apply(finals, GameLogSplits.Split.Home))} home · "
                  + $"{GameLogSplits.FormatRecord(GameLogSplits.Apply(finals, GameLogSplits.Split.Away))} away",
            perGame,
            covers + fails + pushes == 0 ? "no lines" : Covered(Triple(covers, fails, pushes), covers + fails + pushes, finals.Count),
            overs + unders + totalPushes == 0 ? "no lines" : Covered(Triple(overs, unders, totalPushes), overs + unders + totalPushes, finals.Count),
            rest,
            finals,
            last is null ? null : LastGame(last),
            starter is { } s ? (s.Role.Length > 0 ? $"{s.Role} {s.Name}" : s.Name) : null);
    }

    /// <summary>"W 27-20 vs DAL", "L 3-5 at NYY".</summary>
    public static string LastGame(TeamGameLogRow game)
    {
        var mark = game.Won switch { true => "W", false when game.ScoreFor == game.ScoreAgainst => "T", false => "L", _ => "" };
        var where = game.Home ? "vs" : "at";
        var opponent = string.IsNullOrWhiteSpace(game.OpponentAbbrev) ? game.Opponent : game.OpponentAbbrev;
        var score = game.ScoreFor is { } f && game.ScoreAgainst is { } a ? $" {f}-{a}" : "";

        return $"{mark}{score} {where} {opponent}".Trim();
    }

    private static string Triple(int a, int b, int c) => c > 0 ? $"{a}-{b}-{c}" : $"{a}-{b}";

    private static string Average(IEnumerable<int> values)
        => values.Average().ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// A record against a line, with how many finals it covers: "3-1 · 4 of 12 lined". A record
    /// over the few games a closing line was captured for is not the season's, and said bare it
    /// reads as one.
    /// </summary>
    private static string Covered(string record, int graded, int finals)
        => graded == finals ? record : $"{record} · {graded} of {finals} lined";

    /// <summary>
    /// Rest in league days, the leagues' own calendar dates apart (LeagueClock): a night game then
    /// the next day's afternoon game is a day's rest, and Sunday night to the next Sunday's 1pm is
    /// seven. Elapsed hours floored said "same day" and six. The same date is a doubleheader.
    /// </summary>
    private static string Days(int days)
        => days <= 0 ? "same day" : days == 1 ? "1 day" : $"{days} days";
}
