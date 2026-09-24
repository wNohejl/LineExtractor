using LineOps.Core.Entities;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Components.Panels;

/// <summary>
/// Shared plumbing for a windowed panel.
///
/// A panel is an ordinary component — it does not know how it is displayed. Its only
/// coupling to the window system is the cascaded id, which lets it push its own state
/// onto its title bar and rail chip. That inversion is what makes "any sub-page can be
/// a window" true: panels are hostable anywhere, and the chrome reads from them.
///
/// <para>
/// A panel that names the data it draws from (<see cref="Watches"/>) is refreshed when that data
/// changes, whichever process wrote it: <see cref="DeskSignals"/> carries the database's change
/// notices, and the panel reloads through <see cref="RefreshAsync"/>. That is what makes a
/// minimised window's pulse mean something — before it, every window showed the data as of the
/// last click.
/// </para>
/// </summary>
public abstract class PanelBase : ComponentBase, IDisposable
{
    /// <summary>
    /// How long a panel waits after a change before reloading. A backfill or a live poll writes
    /// in bursts; one reload per burst is the point, and a second's lag is invisible.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private bool _subscribed;
    private bool _refreshQueued;
    private bool _disposed;

    [CascadingParameter(Name = "WindowId")]
    protected string? WindowId { get; set; }

    [Inject] protected WindowManager Manager { get; set; } = default!;

    [Inject] private DeskSignals Signals { get; set; } = default!;

    /// <summary>The data topics this panel draws from (<c>DataTopics</c>). Empty: never refreshed.</summary>
    protected virtual IReadOnlySet<string> Watches { get; } = new HashSet<string>();

    /// <summary>Reloads the panel's data after a change it watches. The re-render is done for it.</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>For a panel with its own subscriptions to release. Called once, on disposal.</summary>
    protected virtual void OnDispose()
    {
    }

    public override Task SetParametersAsync(ParameterView parameters)
    {
        // Here rather than in OnInitialized, which every panel overrides and would have to
        // remember to pass on.
        if (!_subscribed && Watches.Count > 0)
        {
            Signals.Changed += OnSignal;
            _subscribed = true;
        }

        return base.SetParametersAsync(parameters);
    }

    private void OnSignal(IReadOnlySet<string> topics)
    {
        if (_disposed || !topics.Overlaps(Watches))
            return;

        lock (_gate)
        {
            if (_refreshQueued)
                return;

            _refreshQueued = true;
        }

        _ = RefreshSoonAsync();
    }

    private async Task RefreshSoonAsync()
    {
        await Task.Delay(Settle);

        lock (_gate)
            _refreshQueued = false;

        if (_disposed)
            return;

        try
        {
            await InvokeAsync(async () =>
            {
                if (_disposed)
                    return;

                await RefreshAsync();
                StateHasChanged();
            });
        }
        catch (Exception) when (!_disposed)
        {
            // A refresh nobody asked for must not take the circuit down; the pulse says it
            // failed, and the next change or a click tries again.
            Report(PulseState.Warn, "refresh failed");
        }
        catch (Exception)
        {
            // Disposed while the reload was in flight: nothing left to show it on.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_subscribed)
            Signals.Changed -= OnSignal;

        OnDispose();
        GC.SuppressFinalize(this);
    }

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
    /// In the league's zone, labelled — see <see cref="DisplayTime"/>.
    /// </summary>
    protected static string Starts(DateTimeOffset at) => DisplayTime.Stamp(at);

    /// <summary>Just the day, for columns where the time is noise.</summary>
    protected static string Day(DateTimeOffset at) => DisplayTime.Day(at);

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

    /// <summary>
    /// "Senga v Sale" — the announced starters, away first like the matchup. A side MLB has not
    /// named reads TBD; null when neither is known, so nothing is drawn for a game without them.
    /// </summary>
    protected static string? Probables(Game game)
        => game.AwayProbablePitcher is null && game.HomeProbablePitcher is null
            ? null
            : $"{Surname(game.AwayProbablePitcher)} v {Surname(game.HomeProbablePitcher)}";

    /// <summary>
    /// Everything after the first name, not the last word: the last word of "CJ Van Eyk" is
    /// "Eyk", and of "Vladimir Guerrero Jr." is "Jr.".
    /// </summary>
    private static string Surname(string? fullName)
    {
        if (fullName is null)
            return "TBD";

        var space = fullName.Trim().IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName.Trim()[(space + 1)..];
    }

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
