using MudBlazor;

namespace LineOps.Web.Components.Desk;

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
    /// The general form, for a caller that already holds a state. The four named methods
    /// above are the usual way in — they read as the outcome rather than as a parameter.
    /// </summary>
    public void Show(string message, DeskState state = DeskState.Neutral)
        => snackbar.Add(message, SeverityFor(state), options => options.SnackbarTypeClass = ClassFor(state));

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
