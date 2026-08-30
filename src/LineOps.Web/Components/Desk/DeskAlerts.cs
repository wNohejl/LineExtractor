using MudBlazor;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.
///
/// Asking the operator to decide, in one line at the call site.
///
/// <para>
/// Without this, every confirmation is six lines of DialogParameters and a cast, which
/// is enough friction that call sites quietly skip the confirmation instead. The point
/// of the service is that guarding a destructive action costs one <c>await</c>.
/// </para>
/// </summary>
public interface IDeskAlerts
{
    /// <summary>
    /// Puts a question to the operator and waits. Returns true if they confirmed.
    /// </summary>
    /// <param name="heading">The question, as a question.</param>
    /// <param name="message">The consequence, in a sentence. Omit when the heading says it all.</param>
    /// <param name="confirmLabel">Name the consequence — "Delete run", not "OK".</param>
    /// <param name="cancelLabel">Null for a one-button alert that only needs acknowledging.</param>
    /// <param name="destructive">Renders the confirm red, and never as the default.</param>
    Task<bool> ConfirmAsync(
        string heading,
        string? message = null,
        string confirmLabel = "OK",
        string? cancelLabel = "Cancel",
        bool destructive = false);
}

/// <inheritdoc />
public sealed class DeskAlerts(IDialogService dialogs) : IDeskAlerts
{
    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        string heading,
        string? message = null,
        string confirmLabel = "OK",
        string? cancelLabel = "Cancel",
        bool destructive = false)
    {
        var parameters = new DialogParameters<DeskAlert>
        {
            { x => x.Heading, heading },
            { x => x.Message, message },
            { x => x.ConfirmLabel, confirmLabel },
            { x => x.CancelLabel, cancelLabel },
            { x => x.Destructive, destructive }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.ExtraSmall,
            CloseOnEscapeKey = true,
            BackdropClick = false // a decision is not dismissed by missing the alert
        };

        var dialog = await dialogs.ShowAsync<DeskAlert>(string.Empty, parameters, options);
        var result = await dialog.Result;

        // Escape and every other dismissal route go through MudBlazor's own cancel, which
        // never reaches the component's handlers — so an unanswered question is a "no",
        // and only an explicit confirm reads as true.
        return result is { Canceled: false, Data: true };
    }
}
