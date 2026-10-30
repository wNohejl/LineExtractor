using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// The window a floating follow-up dialog sits over, when this panel is hosted in one
    /// (FollowUpLayer). The dialog has no window of its own, so <see cref="WindowId"/> is null
    /// there and status goes nowhere; unsaved input still goes to the host, because closing the
    /// host takes the dialog — and what was typed in it — with it.
    /// </summary>
    [CascadingParameter(Name = "HostWindowId")]
    protected string? HostWindowId { get; set; }

    /// <summary>The host this panel last marked as holding its unsaved input, cleared on disposal.</summary>
    private string? _markedHost;

    [Inject] protected WindowManager Manager { get; set; } = default!;

    [Inject] private DeskSignals Signals { get; set; } = default!;

    [Inject] private ILoggerFactory Loggers { get; set; } = default!;

    private ILogger? _log;

    // The last load, kept so Try again repeats exactly what failed — filters and all — rather
    // than each panel remembering which of its loads it was in.
    private Func<Task>? _lastWork;
    private Func<PulseState>? _lastSettle;
    private Func<string?>? _lastStatus;
    private int _generation;

    /// <summary>The panel's own log, named after the panel, for what the operator is not shown.</summary>
    protected ILogger Log => _log ??= Loggers.CreateLogger(GetType());

    /// <summary>
    /// What the panel holds, as the subject of a sentence — "The board", "The runs". The failure
    /// line is built from it, so it names what did not arrive rather than reporting an absence.
    /// </summary>
    protected virtual string LoadSubject => "This window";

    /// <summary>
    /// The sentence the panel shows when its last load failed, or null. Never the exception's
    /// own text: that is written for whoever reads the log, and it goes there.
    /// </summary>
    protected string? LoadError { get; private set; }

    /// <summary>The last load failed. The panel shows <see cref="LoadError"/> and a retry.</summary>
    protected bool Failed => LoadError is not null;

    /// <summary>
    /// A load has completed at least once. Before it the panel holds its shape with placeholders:
    /// an empty result means "nothing here" only once there has been a result.
    /// </summary>
    protected bool Loaded { get; private set; }

    /// <summary>The data topics this panel draws from (<c>DataTopics</c>). Empty: never refreshed.</summary>
    protected virtual IReadOnlySet<string> Watches { get; } = new HashSet<string>();

    /// <summary>
    /// Reloads the panel's data after a change it watches, or one it asked for with
    /// <see cref="RequestRefresh"/>. The re-render is done for it.
    /// </summary>
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
        if (!topics.Overlaps(Watches))
            return;

        RequestRefresh();
    }

    /// <summary>
    /// Asks for a <see cref="RefreshAsync"/> after the settle, as a database change notice does.
    /// For a panel that hears of changes some other way — a service's own event — and would
    /// otherwise reload once per notice in a burst. Safe from any thread: requests inside the
    /// settle fold into the one reload already queued, and the reload runs on the renderer.
    /// </summary>
    protected void RequestRefresh()
    {
        if (_disposed)
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

        // A dialog closed with its input still unsaved takes the input with it; the host it
        // marked has nothing left to protect.
        if (_markedHost is not null)
            Manager.SetUnsaved(_markedHost, false);

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
    /// Says whether the panel holds input that has not been saved, and what to call it —
    /// "this journal entry". While it does, its window's close key asks before discarding it and
    /// eviction passes the window by. Call it as the input changes, and with false once it is
    /// saved or cleared. In a follow-up dialog it marks the window the dialog sits over
    /// (<see cref="HostWindowId"/>). No-op when hosted outside any window.
    /// </summary>
    protected void MarkUnsaved(bool dirty, string? label = null)
    {
        if (WindowId is not null)
        {
            Manager.SetUnsaved(WindowId, dirty, label);
        }
        else if (HostWindowId is not null)
        {
            Manager.SetUnsaved(HostWindowId, dirty, label);
            _markedHost = dirty ? HostWindowId : null;
        }
    }

    /// <summary>
    /// Runs a load with the pulse showing activity, then settles it on the outcome.
    /// Wrapping it here means every panel reports progress the same way rather than each
    /// remembering to.
    ///
    /// <para>
    /// A failure is caught, logged and kept: the pulse goes Critical, <see cref="LoadError"/>
    /// holds a sentence for the operator, and <see cref="RetryAsync"/> repeats the load. It is
    /// not rethrown, because an exception escaping a lifecycle method ends the circuit — the
    /// whole desk, not this window. So this is for reads only: a write whose caller must know it
    /// failed handles its own failure.
    /// </para>
    /// </summary>
    protected async Task WithActivityAsync(Func<Task> work, Func<PulseState> settle, Func<string?>? status = null)
    {
        _lastWork = work;
        _lastSettle = settle;
        _lastStatus = status;

        // Loads overlap — a filter changed while a refresh was in flight — and the one that
        // finishes last is not always the one asked for last. Only the latest started may say
        // how the panel stands; an older one that lands after it is logged and ignored.
        var generation = Interlocked.Increment(ref _generation);

        Report(PulseState.Active);

        try
        {
            await work();

            if (generation != Volatile.Read(ref _generation))
                return;

            LoadError = null;
            Loaded = true;
            Report(settle(), status?.Invoke());
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "{Panel} did not load", GetType().Name);

            if (generation != Volatile.Read(ref _generation))
                return;

            LoadError = $"{LoadSubject} did not load.";
            Report(PulseState.Critical, "load failed");
        }
    }

    /// <summary>
    /// Repeats the last load. The failure line clears first, so what shows while it runs is the
    /// placeholders, or the rows already held, rather than the sentence about the last attempt.
    /// </summary>
    protected Task RetryAsync()
    {
        if (_lastWork is null || _lastSettle is null)
            return Task.CompletedTask;

        LoadError = null;
        return WithActivityAsync(_lastWork, _lastSettle, _lastStatus);
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
    protected static string Surname(string? fullName)
    {
        if (fullName is null)
            return "TBD";

        var space = fullName.Trim().IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName.Trim()[(space + 1)..];
    }

    /// <summary>A game named in full, with its start date — for pickers listing many games.</summary>
    protected static string MatchupWithDate(Game? game)
        => game is null ? "—" : $"{Matchup(game)} — {Day(game.StartsAt)}";

    /// <summary>
    /// What a game's window says after its title: where the game stands. The title is already
    /// the matchup, so repeating it here read as the same name twice. "Final · NYM 4–2 ATL"
    /// once there is a result, the start for a game not yet played.
    /// </summary>
    protected static string StateStatus(Game game)
        => Scoreline.Has(game) ? $"{Phase(game)} · {Scoreline.Format(game)}" : Phase(game);

    /// <summary>Where a game stands in a word, or when it starts if it has not.</summary>
    protected static string Phase(Game game) => game.Status switch
    {
        GameStatus.Live => "Live",
        GameStatus.Final => "Final",
        GameStatus.Postponed => "Postponed",
        _ => Starts(game.StartsAt)
    };

    /// <summary>
    /// What a team's window says after its title: the season's record, "5-0 · 2026". Counted
    /// over the whole season rather than the split on screen, so the bar holds still while a
    /// filter changes.
    /// </summary>
    protected static string StateStatus(IReadOnlyList<TeamGameLogRow> season, string? seasonLabel)
    {
        var record = GameLogSplits.FormatRecord(season);

        return seasonLabel is null ? record : $"{record} · {seasonLabel}";
    }

    protected static string Price(int american) => american > 0 ? $"+{american}" : american.ToString();

    /// <summary>
    /// A line as a book prints it: "-3", "+4.5". A line read back from the journal carries the
    /// column's scale, and decimal's own ToString printed it as "-3.00".
    /// </summary>
    protected static string Line(decimal value) => value.ToString("+0.##;-0.##;0");

    protected static string Age(double minutes) => minutes switch
    {
        < 1 => "just now",
        < 60 => $"{minutes:F0}m",
        < 1440 => $"{minutes / 60:F1}h",
        _ => $"{minutes / 1440:F1}d"
    };
}
