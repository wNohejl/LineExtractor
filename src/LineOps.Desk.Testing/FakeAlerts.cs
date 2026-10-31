using LineOps.Desk.Primitives;

namespace LineOps.Desk.Testing;

/// <summary>A confirm that answers as told and counts the questions.</summary>
public sealed class FakeAlerts : IDeskAlerts
{
    public bool Answer { get; set; } = true;
    public List<string> Asked { get; } = [];

    /// <summary>The confirm key each question offered, in the order asked.</summary>
    public List<string> ConfirmLabels { get; } = [];

    public Task<bool> ConfirmAsync(string heading, string confirmLabel, string? message = null,
        string? cancelLabel = "Cancel", bool destructive = false)
    {
        Asked.Add(heading);
        ConfirmLabels.Add(confirmLabel);
        return Task.FromResult(Answer);
    }
}
