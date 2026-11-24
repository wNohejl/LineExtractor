namespace XLines.Desk.Primitives;

/// <summary>
/// The desk's transient notices: one stack per circuit, drawn by <see cref="DeskToastStack"/>.
///
/// A toast is the quietest tier of feedback the console has. It reports that something finished
/// while the operator was looking at something else, or that the desk did something on its own
/// (closed a window, refused an open) with the way back. Anything that needs a decision stays
/// inline in the panel where the decision is made, or is an alert.
///
/// This type is the seam. Panels call <c>Toasts.Success(...)</c>; they never draw a notice, never
/// name a colour and never write a CSS class. A notice states its <see cref="DeskState"/>, the
/// same name-the-state-not-the-colour rule a tag states, and the state decides its icon, its ink,
/// whether it is announced politely or at once, and how long it stays.
///
/// <para>
/// How long it stays (ADR 0028): a success, an info or a neutral notice without a key goes after
/// <see cref="BriefLife"/>, a warning without a key after <see cref="WarningLife"/>. A failure, and
/// a key that is the only way forward, stays until it is acted on or dismissed: such a key on a timer
/// is a key the operator was not given time to press. A key that only undoes (Undo, Reopen) goes after
/// <see cref="UndoLife"/>, since Ctrl+Z and Back still undo. Every clock stops while the pointer or focus
/// is on the stack (<see cref="Held"/>), and a notice left with less than <see cref="AfterHold"/>
/// gets that long once they leave.
/// </para>
///
/// <para>
/// It no longer goes through MudBlazor's snackbar: the snackbar listed notices in a corner, one
/// under another, over the header's controls. The desk draws its own stack, layered as a tab
/// group is (ADR 0027, 0028), so this class owns the list and the clocks.
/// </para>
/// </summary>
public sealed class DeskToasts : IDisposable
{
    /// <summary>How long a success, an info or a neutral notice without a key stays.</summary>
    public static readonly TimeSpan BriefLife = TimeSpan.FromSeconds(5);

    /// <summary>How long a warning without a key stays: long enough to be read twice.</summary>
    public static readonly TimeSpan WarningLife = TimeSpan.FromSeconds(8);

    /// <summary>How long a notice whose key only undoes something stays: a warning's time.</summary>
    public static readonly TimeSpan UndoLife = TimeSpan.FromSeconds(8);

    /// <summary>The least a timed notice is given once the pointer and focus leave the stack.</summary>
    public static readonly TimeSpan AfterHold = TimeSpan.FromSeconds(2);

    /// <summary>The most kept at once. Past it the oldest timed notice goes first, then the oldest of any; never the one being raised.</summary>
    public const int MaxKept = 12;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly List<DeskToast> _shown = [];
    private readonly Dictionary<long, Clock> _clocks = [];
    private ITimer? _timer;
    private long _sequence;
    private bool _held;
    private bool _disposed;

    private sealed class Clock
    {
        public DateTimeOffset? DueAt;
        public TimeSpan Remaining;
    }

    public DeskToasts()
        : this(TimeProvider.System)
    {
    }

    /// <summary>For a host or a test that says where time comes from.</summary>
    public DeskToasts(TimeProvider time) => _time = time;

    /// <summary>Raised whenever the stack changes: a notice raised, dismissed, acted on or expired. Off the render thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with a notice as it leaves the stack, for whatever reason.</summary>
    public event Action<DeskToast>? Departed;

    /// <summary>The notices standing, newest first: the one in front, then the ones layered behind it.</summary>
    public IReadOnlyList<DeskToast> Shown
    {
        get
        {
            lock (_gate)
                return _shown.ToList();
        }
    }

    /// <summary>
    /// The last notice raised, as the desk said it: words, tone and key. Kept after it leaves the
    /// stack, so a test, or anything else on the circuit, reads what was said.
    /// </summary>
    public DeskToast? Last { get; private set; }

    /// <summary>Work finished and the outcome was healthy.</summary>
    public void Success(string message) => Show(message, DeskState.Positive);

    /// <summary>Something happened worth knowing and nothing is wrong.</summary>
    public void Info(string message) => Show(message, DeskState.Info);

    /// <summary>Finished, but with a cost the operator should know about.</summary>
    public void Warn(string message) => Show(message, DeskState.Warning);

    /// <summary>
    /// Background work did not complete. Flag failures here only when the operator can simply try
    /// again — a failure that needs a decision belongs inline, next to the decision. It stays until
    /// dismissed, and is announced at once rather than politely.
    /// </summary>
    public void Fail(string message) => Show(message, DeskState.Negative);

    /// <summary>
    /// Something was added — an item to a list, a line to a group — with a key to go and see it
    /// where there is somewhere to go.
    /// </summary>
    public void Added(string message, string? actionLabel = null, Action? action = null)
        => Show(message, DeskState.Positive, actionLabel, action);

    /// <summary>
    /// Something was taken away — an item dropped from the list — with Undo when the caller can put
    /// it back: a removal the operator did not mean is one press from undone, and needs no
    /// confirmation first (HIG, Undo and redo).
    /// </summary>
    /// <remarks>A removal wears the removal's red but is not a failure: without Undo it goes on its own.</remarks>
    public void Removed(string message, Action? undo = null)
        => RaiseCore(message, DeskState.Negative, undo is null ? null : "Undo", undo, failure: false, reverses: undo is not null);

    /// <summary>
    /// The general form, for a caller that already holds a state. The named methods above are the
    /// usual way in — they read as the outcome rather than as a parameter. An action is one key on
    /// the toast; pressing it dismisses the toast and runs the action.
    /// </summary>
    public void Show(string message, DeskState state = DeskState.Neutral, string? actionLabel = null, Action? action = null)
        => Raise(message, state, actionLabel, action);

    /// <summary><see cref="Show"/>, returning the notice raised so its owner can take it down again.</summary>
    public DeskToast Raise(string message, DeskState state = DeskState.Neutral, string? actionLabel = null, Action? action = null)
        => RaiseCore(message, state, actionLabel, action, failure: state == DeskState.Negative, reverses: false);

    /// <summary>
    /// A notice whose key only takes back what was just done — Undo, Reopen — so the way back is
    /// also elsewhere (Ctrl+Z, Back) and the notice need not stand over the desk until pressed: it
    /// goes after <see cref="UndoLife"/>, held while it is being read like any other.
    /// </summary>
    public DeskToast RaiseUndo(string message, string actionLabel, Action undo, DeskState state = DeskState.Info)
        => RaiseCore(message, state, actionLabel, undo, failure: false, reverses: true);

    private DeskToast RaiseCore(string message, DeskState state, string? actionLabel, Action? action, bool failure, bool reverses)
    {
        DeskToast toast;
        List<DeskToast> dropped;

        lock (_gate)
        {
            var key = action is null ? null : actionLabel;
            toast = new DeskToast(message, state, key, key is null ? null : action) { Id = ++_sequence, Failure = failure, Reverses = reverses && key is not null };

            _shown.Insert(0, toast);

            if (LifeOf(toast) is { } life)
            {
                var clock = new Clock { Remaining = life };
                if (!_held)
                    clock.DueAt = _time.GetUtcNow() + life;

                _clocks[toast.Id] = clock;
            }

            dropped = Trim(toast);
            Last = toast;
            Schedule();
        }

        foreach (var gone in dropped)
            Departed?.Invoke(gone);

        Changed?.Invoke();
        return toast;
    }

    /// <summary>
    /// How long a notice stands before it goes on its own, or null for one that stays until it is
    /// acted on or dismissed: a failure, or a key that is the only way forward. A key that only
    /// takes something back (<see cref="RaiseUndo"/>, <see cref="Removed"/> with Undo) is timed at
    /// <see cref="UndoLife"/>: Ctrl+Z and Back still undo once it has gone.
    /// </summary>
    public static TimeSpan? LifeOf(DeskToast toast)
    {
        if (toast.Failure)
            return null;

        if (toast.Action is not null)
            return toast.Reverses ? UndoLife : null;

        return toast.State == DeskState.Warning ? WarningLife : BriefLife;
    }

    /// <summary>Takes one notice down.</summary>
    public void Dismiss(DeskToast toast)
    {
        bool removed;

        lock (_gate)
        {
            removed = _shown.Remove(toast);
            _clocks.Remove(toast.Id);
            Schedule();
        }

        if (!removed)
            return;

        Departed?.Invoke(toast);
        Changed?.Invoke();
    }

    /// <summary>Takes every notice down: the stack's Clear all.</summary>
    public void DismissAll()
    {
        List<DeskToast> all;

        lock (_gate)
        {
            all = _shown.ToList();
            _shown.Clear();
            _clocks.Clear();
            Schedule();
        }

        if (all.Count == 0)
            return;

        foreach (var toast in all)
            Departed?.Invoke(toast);

        Changed?.Invoke();
    }

    /// <summary>Presses a notice's key: the notice goes, then its action runs.</summary>
    public void Act(DeskToast toast)
    {
        Dismiss(toast);
        toast.Action?.Invoke();
    }

    /// <summary>
    /// The pointer or focus is on the stack: every clock stops, so nothing being read or reached
    /// for leaves under it. Set false again when both have left.
    /// </summary>
    public bool Held
    {
        get
        {
            lock (_gate)
                return _held;
        }
        set
        {
            lock (_gate)
            {
                if (_held == value)
                    return;

                _held = value;
                var now = _time.GetUtcNow();

                foreach (var clock in _clocks.Values)
                {
                    if (value)
                    {
                        if (clock.DueAt is { } due)
                            clock.Remaining = due > now ? due - now : TimeSpan.Zero;

                        clock.DueAt = null;
                    }
                    else
                    {
                        clock.DueAt = now + (clock.Remaining < AfterHold ? AfterHold : clock.Remaining);
                    }
                }

                Schedule();
            }
        }
    }

    // Past the most kept, the oldest timed notice goes first: it was going anyway, and a notice
    // with a key is one the operator may still need. Then the oldest that is not a failure — a
    // failure stands until it is read, and dropping one silently is the one loss nobody would
    // know to look for. Only when every other notice is a failure does the oldest of any go.
    // Never the one just raised: it is the one nobody has read yet, and an owner holding it
    // (Raise) would be told it left before it had it.
    private List<DeskToast> Trim(DeskToast raised)
    {
        var dropped = new List<DeskToast>();

        while (_shown.Count > MaxKept)
        {
            var victim = _shown.LastOrDefault(t => t.Id != raised.Id && _clocks.ContainsKey(t.Id))
                ?? _shown.LastOrDefault(t => t.Id != raised.Id && !t.Failure)
                ?? _shown.Last(t => t.Id != raised.Id);
            _shown.Remove(victim);
            _clocks.Remove(victim.Id);
            dropped.Add(victim);
        }

        return dropped;
    }

    // One timer, set for the soonest notice due. Called under the gate.
    private void Schedule()
    {
        _timer?.Dispose();
        _timer = null;

        if (_disposed)
            return;

        var next = _clocks.Values.Where(c => c.DueAt is not null).Select(c => c.DueAt!.Value).DefaultIfEmpty().Min();
        if (next == default)
            return;

        var wait = next - _time.GetUtcNow();
        _timer = _time.CreateTimer(_ => Expire(), null, wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void Expire()
    {
        List<DeskToast> gone;

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            gone = _shown.Where(t => _clocks.TryGetValue(t.Id, out var c) && c.DueAt is { } due && due <= now).ToList();

            foreach (var toast in gone)
            {
                _shown.Remove(toast);
                _clocks.Remove(toast.Id);
            }

            Schedule();
        }

        if (gone.Count == 0)
            return;

        foreach (var toast in gone)
            Departed?.Invoke(toast);

        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}

/// <summary>
/// A notice as the desk raised it: its words, its tone, and its one key if it has one. Each raise
/// is its own notice (<see cref="Id"/>), so the same words said twice are two notices, and taking
/// one down never takes the other.
/// </summary>
public sealed record DeskToast(string Message, DeskState State, string? ActionLabel, Action? Action)
{
    /// <summary>Its place in the order raised on this circuit; higher is newer.</summary>
    public long Id { get; init; }

    /// <summary>
    /// Work that did not complete (<see cref="DeskToasts.Fail"/>, or any notice raised in the
    /// negative state but a removal): it stays until dismissed and is announced at once.
    /// </summary>
    public bool Failure { get; init; }

    /// <summary>Its key only takes back what was just done (Undo, Reopen); such a notice is timed.</summary>
    public bool Reverses { get; init; }

    /// <summary>A failure is announced at once (<c>role="alert"</c>); everything else politely.</summary>
    public bool Assertive => Failure;
}
