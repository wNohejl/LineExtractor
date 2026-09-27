using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Web.Services;

/// <summary>
/// A side named the way the desk writes it: "ML · Chiefs", "Spread · Bills +2.5". One
/// definition, because the chip that picked it, the slip card that holds it and the sentence
/// that says it was already logged must all read the same.
/// </summary>
public static class SelectionText
{
    public static string MarketName(string market) => market switch
    {
        Markets.Moneyline => "ML",
        Markets.Spread => "Spread",
        Markets.Total => "Total",
        _ => market
    };

    /// <summary>Team names are long and these are buttons; the last word identifies a club.</summary>
    public static string Short(string outcome)
        => outcome.Contains(' ')
           && !outcome.Equals("over", StringComparison.OrdinalIgnoreCase)
           && !outcome.Equals("under", StringComparison.OrdinalIgnoreCase)
            ? outcome[(outcome.LastIndexOf(' ') + 1)..]
            : outcome;

    public static string Of(string market, string outcome, decimal? line)
        => $"{MarketName(market)} · {Short(outcome)}{(line is { } l ? " " + OddsMath.FormatLine(l, outcome) : "")}";
}
