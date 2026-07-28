using LineOps.Core.Entities;
using LineOps.Web.Windowing;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Components.Panels;

/// <summary>
/// Shared plumbing for a windowed panel.
///
/// A panel is an ordinary component — it does not know how it is displayed. Its only
/// coupling to the window system is the cascaded id, which lets it push its own state
/// onto its title bar and rail chip. That inversion is what makes "any sub-page can be
/// a window" true: panels are hostable anywhere, and the chrome reads from them.
/// </summary>
public abstract class PanelBase : ComponentBase
{
    [CascadingParameter(Name = "WindowId")]
    protected string? WindowId { get; set; }

    [Inject] protected WindowManager Manager { get; set; } = default!;

    /// <summary>Reports this panel's state to its chrome. No-op when hosted outside a window.</summary>
    protected void Report(PulseState state, string? status = null)
    {
        if (WindowId is not null)
            Manager.SetPulse(WindowId, state, status);
    }

    /// <summary>
    /// Runs a load with the pulse showing activity, then settles it on the outcome.
    /// Wrapping it here means every panel reports progress the same way rather than each
    /// remembering to.
    /// </summary>
    protected async Task WithActivityAsync(Func<Task> work, Func<PulseState> settle, Func<string?>? status = null)
    {
        Report(PulseState.Active);

        try
        {
            await work();
            Report(settle(), status?.Invoke());
        }
        catch
        {
            Report(PulseState.Critical, "load failed");
            throw;
        }
    }

    /// <summary>
    /// When a game starts, as a date and time.
    ///
    /// Not the day of week. "Sat 14:30" only reads as a date for the two days either side of
    /// today, and a desk that now holds a full season of games is regularly looking at neither.
    /// A date is unambiguous at any distance, which is what a column of them needs to be.
    /// </summary>
    protected static string Starts(DateTimeOffset at) => at.ToLocalTime().ToString("MMM d HH:mm");

    /// <summary>Just the day, for columns where the time is noise.</summary>
    protected static string Day(DateTimeOffset at) => at.ToLocalTime().ToString("MMM d");

    /// <summary>
    /// A game named in full: "Colorado Rockies at San Francisco Giants".
    ///
    /// Abbreviations are fine in a dense grid where the column is three characters wide and the
    /// same teams repeat down it. They are not fine anywhere a game is being *identified* — a
    /// window title, a picker, the thing you just highlighted — because "CR at SFG" asks the
    /// reader to decode before they can confirm they picked the right game.
    /// </summary>
    protected static string Matchup(Game? game)
        => game is null
            ? "—"
            : $"{game.AwayTeam?.Name ?? "Away"} at {game.HomeTeam?.Name ?? "Home"}";

    /// <summary>A game named in full, with its start date — for pickers listing many games.</summary>
    protected static string MatchupWithDate(Game? game)
        => game is null ? "—" : $"{Matchup(game)} — {Day(game.StartsAt)}";

    protected static string Price(int american) => american > 0 ? $"+{american}" : american.ToString();

    protected static string Line(decimal value) => value > 0 ? $"+{value}" : value.ToString();

    protected static string Age(double minutes) => minutes switch
    {
        < 1 => "just now",
        < 60 => $"{minutes:F0}m",
        < 1440 => $"{minutes / 60:F1}h",
        _ => $"{minutes / 1440:F1}d"
    };
}
