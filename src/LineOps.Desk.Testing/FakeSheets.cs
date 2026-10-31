using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Desk.Testing;

/// <summary>
/// Sheets that are recorded rather than drawn: which sheet was asked for, with what, and a handle
/// the test closes to answer it.
/// </summary>
public sealed class FakeSheets : IDeskSheets
{
    /// <summary>The type of the last sheet shown, or null before any.</summary>
    public Type? Shown { get; private set; }

    /// <summary>The parameters the last sheet was shown with.</summary>
    public IDictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>();

    /// <summary>The handle the last sheet's caller holds; closing it answers the caller.</summary>
    public Handle? Last { get; private set; }

    public Task<IDeskSheetHandle> ShowAsync<TSheet>(DialogParameters<TSheet> parameters, string width = "480px")
        where TSheet : IComponent
    {
        Shown = typeof(TSheet);
        Parameters.Clear();
        foreach (var (name, value) in parameters)
            Parameters[name] = value;

        Last = new Handle();
        return Task.FromResult<IDeskSheetHandle>(Last);
    }

    public sealed class Handle : IDeskSheetHandle
    {
        private readonly TaskCompletionSource<DialogResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DialogResult> Result => _result.Task;

        public void Close() => Close(DialogResult.Cancel());

        public void Close(DialogResult result) => _result.TrySetResult(result);
    }
}
