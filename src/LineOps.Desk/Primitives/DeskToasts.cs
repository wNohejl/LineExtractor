using MudBlazor;

namespace LineOps.Desk.Primitives;

/// <summary>
/// The desk's transient notice.
///
/// A toast is the quietest tier of feedback the console has: non-critical, auto-dismissing,
/// and recoverable elsewhere. It reports that something finished while the operator was
/// looking at something else. Anything that needs a decision, or that the operator must not
/// miss, stays inline in the panel where the decision is made — a toast that has scrolled
/// away cannot be acted on.
///
/// This type is the seam. Panels call <c>Toasts.Success(...)</c>; they never take
/// <c>ISnackbar</c>, never name a MudBlazor <c>Severity</c>, and never write a CSS class.
/// That keeps the call sites reviewable in the desk's own vocabulary — a notice states its
/// <see cref="DeskState"/>, the same name-the-state-not-the-colour rule a tag states — and it
/// leaves one place to change if the toast is ever rendered by something other than MudBlazor.
///
/// The state drives two things: which desk hue the notice wears (through
/// <c>SnackbarTypeClass</c>, styled in css/mud-bridge.css) and which icon MudBlazor picks
/// for it. Both come from the state, so a call site cannot get them out of step.
/// </summary>
public sealed class DeskToasts(ISnackbar snackbar)
{
    /// <summary>Work finished and the outcome was healthy.</summary>
    public void Success(string message) => Show(message, DeskState.Positive);

    /// <summary>Something happened worth knowing and nothing is wrong.</summary>
    public void Info(string message) => Show(message, DeskState.Info);

    /// <summary>Finished, but with a cost the operator should know about.</summary>
    public void Warn(string message) => Show(message, DeskState.Warning);

    /// <summary>
    /// Background work did not complete. Flag failures here only when the operator can
    /// simply try again — a failure that needs a decision belongs inline, next to the
    /// decision.
    /// </summary>
    public void Fail(string message) => Show(message, DeskState.Negative);

    /// <summary>
    /// Something was added — a wager to the slip, a leg to a parlay. Green, so an addition reads
    /// as one at a glance, with a key to go and see it where there is somewhere to go. An
    /// addition used to be a grey note above the form that stayed until the next one, the same
    /// grey as every other note, so nothing said a thing had been added.
    /// </summary>
    public void Added(string message, string? actionLabel = null, Action? action = null)
        => Show(message, DeskState.Positive, actionLabel, action);

    /// <summary>
    /// Something was taken away — a wager dropped from the slip. Red, the removal's colour, with
    /// Undo when the caller can put it back: a removal the operator did not mean is one press
    /// from undone, and needs no confirmation first (HIG, Undo and redo).
    /// </summary>
    public void Removed(string message, Action? undo = null)
        => Show(message, DeskState.Negative, undo is null ? null : "Undo", undo);

    /// <summary>
    /// The general form, for a caller that already holds a state. The named methods above are
    /// the usual way in — they read as the outcome rather than as a parameter. An action is one
    /// key on the toast; pressing it runs the action and dismisses the toast.
    /// </summary>
    public void Show(string message, DeskState state = DeskState.Neutral, string? actionLabel = null, Action? action = null)
    {
        Last = new DeskToast(message, state, action is null ? null : actionLabel, action);

        snackbar.Add(message, SeverityFor(state), options =>
        {
            options.SnackbarTypeClass = ClassFor(state);

            if (actionLabel is not null && action is not null)
            {
                options.Action = actionLabel;
                options.OnClick = _ =>
                {
                    action();
                    return Task.CompletedTask;
                };
            }
        });
    }

    /// <summary>
    /// The last notice raised, as the desk said it: words, tone and key. The snackbar keeps its
    /// options to itself, so this is how a test, or anything else on the circuit, reads what was
    /// said without reaching into MudBlazor.
    /// </summary>
    public DeskToast? Last { get; private set; }

    /// <summary>
    /// MudBlazor's severity is used for its icon only — every colour a notice wears comes
    /// from the desk's own state class. Neutral maps to Normal, which draws no icon at all.
    /// </summary>
    private static Severity SeverityFor(DeskState state) => state switch
    {
        DeskState.Positive => Severity.Success,
        DeskState.Info => Severity.Info,
        DeskState.Warning => Severity.Warning,
        DeskState.Negative => Severity.Error,
        _ => Severity.Normal
    };

    private static string ClassFor(DeskState state) => state switch
    {
        DeskState.Positive => "desk-toast desk-toast--go",
        DeskState.Info => "desk-toast desk-toast--action",
        DeskState.Warning => "desk-toast desk-toast--caution",
        DeskState.Negative => "desk-toast desk-toast--stop",
        _ => "desk-toast desk-toast--neutral"
    };
}

/// <summary>A notice as the desk raised it: its words, its tone, and its one key if it has one.</summary>
public sealed record DeskToast(string Message, DeskState State, string? ActionLabel, Action? Action);
