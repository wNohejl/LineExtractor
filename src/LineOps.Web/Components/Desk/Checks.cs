using System.Globalization;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Services;

namespace LineOps.Web.Components.Desk;

/// <summary>One advisory row: a state, one sentence, and optionally a remedy key on the same row.</summary>
public sealed record Check(DeskState State, string Text, string? Remedy = null, Func<Task>? Act = null);

/// <summary>
/// What a remedy key does, supplied by the window that renders the strip. The builder is pure; it
/// cannot run a pull or open a window, only say which key belongs on which row.
/// </summary>
public sealed record CheckActions(string? PullLabel = null, Func<Task>? Pull = null, Func<Task>? OpenJournal = null);

/// <summary>
/// The checks the wager window runs before Log (spec §4). Each is keyed on what it reads — the
/// selection, the odds, the journal — and none reads the stake except to say what the value comes
/// to. Only the closing line blocks: it is a fact about the price, not a judgment the operator
/// may overrule. Sentences state a fact and its consequence; they never instruct.
/// </summary>
public static class Checks
{
    /// <summary>
    /// When a price stops being fresh. A line scan costs credits and is run by hand; half an hour
    /// is the shortest interval at which spending two credits is a reasonable thing to suggest.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>How far back a bet on the same side counts as already logged.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromDays(7);

    public const string OpenJournal = "Open Journal";

    /// <summary>
    /// Value, then price age (or the closing line, or a typed price, which replace it), then the
    /// journal. No start time: a bet on no game, which has no clock to be late against.
    /// </summary>
    public static IReadOnlyList<Check> Build(
        WagerOffer offer, DateTimeOffset? startsAt, DateTimeOffset now,
        bool priorRead, JournalEntry? prior, decimal stake, CheckActions? actions = null,
        bool priorFailed = false)
    {
        actions ??= new CheckActions();

        return
        [
            Value(offer, stake),
            offer.Manual ? Typed(startsAt, now)
                : offer.IsClosing ? Closing(startsAt ?? now, now, actions)
                : Age(offer, now, actions),
            Duplicate(priorRead, prior, actions, priorFailed)
        ];
    }

    public static Check Value(WagerOffer offer, decimal stake)
    {
        if (offer.Ev is not { } ev)
            return offer.Manual
                ? new Check(DeskState.Neutral, "No fair price at this number, so value is unread rather than zero.")
                : new Check(DeskState.Neutral,
                    "No fair price on this number: fewer than two books quote it, so value is unread rather than zero.");

        var against = offer.Fair is { } fair
            ? $" against fair {OddsMath.FormatPrice(fair.PriceAmerican)} ({Basis(fair)}, no-vig)"
            : " against the fair price";

        var expected = stake > 0
            ? $" · {Signed((decimal)ev * stake)} expected on {stake.ToString("C0", CultureInfo.CurrentCulture)}"
            : "";

        return ev > 0
            ? new Check(DeskState.Positive, $"Value: {Percent(ev)}{against}{expected}.")
            : new Check(DeskState.Warning, $"No value: {Percent(ev)}{against}{expected}.");
    }

    public static Check Age(WagerOffer offer, DateTimeOffset now, CheckActions actions)
    {
        var age = now - offer.CapturedAt;

        if (age < StaleAfter)
            return new Check(DeskState.Positive,
                age < TimeSpan.FromMinutes(1) ? "Price captured just now." : $"Price captured {Span(age)} ago.");

        var canPull = actions.PullLabel is not null && actions.Pull is not null;

        return new Check(DeskState.Warning,
            $"Price captured {Span(age)} ago; the line may have moved since.",
            canPull ? actions.PullLabel : null,
            canPull ? actions.Pull : null);
    }

    /// <summary>
    /// A typed price has no capture time to age: it is the operator's word for what they took. Said
    /// as a fact, with the game's clock where it has started, because a late entry is normal here.
    /// </summary>
    public static Check Typed(DateTimeOffset? startsAt, DateTimeOffset now)
        => startsAt is { } starts && now > starts
            ? new Check(DeskState.Neutral, $"Your own price: the game started {Span(now - starts)} ago, so this records the price you took.")
            : new Check(DeskState.Neutral, "Your own price, entered by hand rather than read from a scan.");

    public static Check Closing(DateTimeOffset startsAt, DateTimeOffset now, CheckActions actions)
    {
        var since = now - startsAt;

        var text = since > TimeSpan.Zero
            ? $"Closing line: the game started {Span(since)} ago, so this price cannot have been taken."
            : "Closing line: this is where the market closed, so this price cannot have been taken.";

        return new Check(DeskState.Negative, text,
            actions.OpenJournal is not null ? OpenJournal : null, actions.OpenJournal);
    }

    /// <summary>
    /// Whether this side is already in the journal. A read that failed is a warning rather than
    /// "reading" for ever: logging stays allowed, but nothing has ruled out a second bet.
    /// </summary>
    public static Check Duplicate(bool read, JournalEntry? prior, CheckActions actions, bool failed = false)
    {
        if (failed)
            return new Check(DeskState.Warning, "The journal could not be read, so a duplicate cannot be ruled out.");

        if (!read)
            return new Check(DeskState.Neutral, "Reading the journal for this side.");

        if (prior is null)
            return new Check(DeskState.Positive, "Nothing logged on this side yet.");

        var what = SelectionText.Of(prior.Market, prior.Outcome, prior.LineTaken);

        return new Check(DeskState.Warning,
            $"Already in the journal: {prior.Stake.ToString("C0", CultureInfo.CurrentCulture)} on {what} at "
            + $"{OddsMath.FormatPrice(prior.PriceTaken)} ({prior.Book}), {DisplayTime.Stamp(prior.PlacedAt)}. "
            + "Logging again records a second bet.",
            actions.OpenJournal is not null ? OpenJournal : null, actions.OpenJournal);
    }

    /// <summary>
    /// An earlier bet named without its side — "$25 at -128 (draftkings), Sep 26 13:18 ET" — for a
    /// place that already shows the side, such as a slip card.
    /// </summary>
    public static string Prior(JournalEntry prior)
        => $"{prior.Stake.ToString("C0", CultureInfo.CurrentCulture)} at {OddsMath.FormatPrice(prior.PriceTaken)} "
           + $"({prior.Book}), {DisplayTime.Stamp(prior.PlacedAt)}";

    /// <summary>"12 min", "2h 14m" — whole minutes, so a threshold reads the same on screen as in a test.</summary>
    public static string Span(TimeSpan span)
        => span.TotalMinutes < 60
            ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalHours}h {span.Minutes}m";

    private static string Basis(FairPrice fair)
        => fair.Basis == FairValue.SharpBook ? "Pinnacle" : $"{fair.Books}-book consensus";

    private static string Percent(double ev)
    {
        var percent = ev * 100;
        return (percent >= 0 ? "+" : "\u2212") + Math.Abs(percent).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Signed(decimal amount)
        => (amount >= 0 ? "+" : "\u2212") + Math.Abs(amount).ToString("C2", CultureInfo.CurrentCulture);
}
