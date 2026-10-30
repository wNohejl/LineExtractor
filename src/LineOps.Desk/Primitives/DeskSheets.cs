using System.Reflection;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Desk.Primitives;

/// <summary>
/// Opening a modal sheet (<see cref="DeskSheet"/>, or a component built on one) without holding
/// MudBlazor's dialog service.
///
/// <para>
/// Beside <see cref="IDeskAlerts"/>, for the tier above it: an alert asks one question, a sheet
/// holds one self-contained task. A caller states the sheet and its parameters; the scrim, the
/// dismissal rules and the width a sheet opens at are decided here, once, rather than copied
/// into every call site as a block of <c>DialogOptions</c>.
/// </para>
///
/// <para>
/// Read the outcome off <see cref="IDeskSheetHandle.Result"/>, never off <c>OnCancel</c>: Escape
/// and a backdrop click go through MudBlazor's own cancel path and never reach the component's
/// callbacks. <c>result is { Canceled: false, Data: … }</c> treats every way out that is not an
/// explicit completion as a "no".
/// </para>
/// </summary>
public interface IDeskSheets
{
    /// <summary>Opens <typeparamref name="TSheet"/> as a modal sheet and returns the handle that closes it.</summary>
    /// <param name="parameters">The sheet's parameters, named by expression.</param>
    /// <param name="width">
    /// The floor for the sheet's body, for a sheet that has a <c>Width</c> parameter as
    /// <see cref="DeskSheet"/> does. A <c>Width</c> already among the parameters wins.
    /// </param>
    Task<IDeskSheetHandle> ShowAsync<TSheet>(DialogParameters<TSheet> parameters, string width = "480px")
        where TSheet : IComponent;
}

/// <summary>An open sheet, as its caller holds it.</summary>
public interface IDeskSheetHandle
{
    /// <summary>How the sheet ended. Canceled for every way out that was not a completion.</summary>
    Task<DialogResult> Result { get; }

    /// <summary>Takes the sheet down as the way out: the result reads as cancelled.</summary>
    void Close();

    /// <summary>Takes the sheet down with an outcome — <c>DialogResult.Ok(…)</c> for a completed task.</summary>
    void Close(DialogResult result);
}

/// <inheritdoc />
public sealed class DeskSheets(IDialogService dialogs) : IDeskSheets
{
    private const string WidthParameter = nameof(DeskSheet.Width);

    /// <inheritdoc />
    public async Task<IDeskSheetHandle> ShowAsync<TSheet>(DialogParameters<TSheet> parameters, string width = "480px")
        where TSheet : IComponent
    {
        if (HasWidth(typeof(TSheet)) && parameters.TryGet<string>(WidthParameter) is null)
            parameters.Add(WidthParameter, width);

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Small,
            CloseOnEscapeKey = true,
            // Missing the sheet costs the draft, not a bad write: every dismissal reads as
            // cancelled, so a stray click outside can be allowed to mean "never mind". An alert
            // does the opposite (DeskAlerts), because a decision must not be dismissed by missing it.
            BackdropClick = true
        };

        var dialog = await dialogs.ShowAsync<TSheet>(string.Empty, parameters, options);
        return new Handle(dialog);
    }

    /// <summary>Whether the sheet takes a string <c>Width</c> parameter, as DeskSheet does.</summary>
    private static bool HasWidth(Type sheet)
        => sheet.GetProperty(WidthParameter, BindingFlags.Public | BindingFlags.Instance) is { } property
           && property.PropertyType == typeof(string)
           && property.IsDefined(typeof(ParameterAttribute));

    private sealed class Handle(IDialogReference dialog) : IDeskSheetHandle
    {
        private Task<DialogResult>? _result;

        public Task<DialogResult> Result => _result ??= ReadAsync();

        // A sheet taken down with no result at all is one nobody completed.
        private async Task<DialogResult> ReadAsync() => await dialog.Result ?? DialogResult.Cancel();

        public void Close() => dialog.Close(DialogResult.Cancel());

        public void Close(DialogResult result) => dialog.Close(result);
    }
}
