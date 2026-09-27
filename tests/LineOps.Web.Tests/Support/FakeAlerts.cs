using LineOps.Desk.Primitives;

namespace LineOps.Web.Tests.Support;

/// <summary>A confirm that answers as told and counts the questions.</summary>
public sealed class FakeAlerts : IDeskAlerts
{
    public bool Answer { get; set; } = true;
    public List<string> Asked { get; } = [];

    public Task<bool> ConfirmAsync(string heading, string? message = null, string confirmLabel = "OK",
        string? cancelLabel = "Cancel", bool destructive = false)
    {
        Asked.Add(heading);
        return Task.FromResult(Answer);
    }
}
