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
/// <see cref="DeskTone"/>, the same consequence-not-colour rule a key states — and it leaves
/// one place to change if the toast is ever rendered by something other than MudBlazor.
///
/// The tone drives two things: which desk hue the notice wears (through
/// <c>SnackbarTypeClass</c>, styled in css/mud-bridge.css) and which icon MudBlazor picks
/// for it. Both come from the tone, so a call site cannot get them out of step.
/// </summary>
public sealed class DeskToasts(ISnackbar snackbar)
{
    /// <summary>Work finished and the outcome was healthy. Steam.</summary>
    public void Success(string message) => Show(message, DeskTone.Go);

    /// <summary>Something happened worth knowing and nothing is wrong. Iris.</summary>
    public void Info(string message) => Show(message, DeskTone.Action);

    /// <summary>Finished, but with a cost the operator should know about. Flag.</summary>
    public void Warn(string message) => Show(message, DeskTone.Caution);

    /// <summary>
    /// Background work did not complete. Flag failures here only when the operator can
    /// simply try again — a failure that needs a decision belongs inline, next to the
    /// decision. Drift.
    /// </summary>
    public void Fail(string message) => Show(message, DeskTone.Stop);

    /// <summary>
    /// The general form, for a caller that already holds a tone. The four named methods
    /// above are the usual way in — they read as the outcome rather than as a parameter.
    /// </summary>
    public void Show(string message, DeskTone tone = DeskTone.Neutral)
        => snackbar.Add(message, SeverityFor(tone), options => options.SnackbarTypeClass = ClassFor(tone));

    /// <summary>
    /// MudBlazor's severity is used for its icon only — every colour a notice wears comes
    /// from the desk's own tone class. Neutral maps to Normal, which draws no icon at all.
    /// </summary>
    private static Severity SeverityFor(DeskTone tone) => tone switch
    {
        DeskTone.Go => Severity.Success,
        DeskTone.Action => Severity.Info,
        DeskTone.Caution => Severity.Warning,
        DeskTone.Stop => Severity.Error,
        _ => Severity.Normal
    };

    private static string ClassFor(DeskTone tone) => tone switch
    {
        DeskTone.Go => "desk-toast desk-toast--go",
        DeskTone.Action => "desk-toast desk-toast--action",
        DeskTone.Caution => "desk-toast desk-toast--caution",
        DeskTone.Stop => "desk-toast desk-toast--stop",
        _ => "desk-toast desk-toast--neutral"
    };
}
