using System.Reflection;
using Bunit;
using XLines.Desk.Windowing;

namespace XLines.Web.Tests.Support;

/// <summary>
/// Waits for a state that another thread brings about — a read reaching the gate, a cancel the
/// panel sends on the renderer's thread — where bUnit's own waits, which re-check on a render,
/// would not notice it.
/// </summary>
public static class Eventually
{
    /// <summary>Generous, because the first query of a run builds the model, and the suite runs in parallel.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Polls until <paramref name="condition"/> holds or <paramref name="patience"/> runs out, then
    /// asserts it, with <paramref name="what"/> saying what was seen when it did not.
    /// </summary>
    public static void WaitFor(Func<bool> condition, Func<string>? what = null, TimeSpan? patience = null)
    {
        var until = DateTime.UtcNow + (patience ?? Patience);
        while (!condition() && DateTime.UtcNow < until)
            Thread.Sleep(25);

        Assert.True(condition(), what?.Invoke());
    }
}

/// <summary>
/// Reaches the state <see cref="DeskPanelBase"/> keeps to itself. The tests read whether a load
/// reported a failure, and place what a load would have read on a panel whose own load cannot run;
/// both are non-public, so they are reached by name.
/// </summary>
public static class PanelProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly PropertyInfo LoadErrorProperty = typeof(DeskPanelBase).GetProperty("LoadError", Hidden)!;
    private static readonly PropertyInfo LoadedProperty = typeof(DeskPanelBase).GetProperty("Loaded", Hidden)!;

    /// <summary>The failure the panel's last load reported, or null.</summary>
    public static string? LoadError(object panel) => (string?)LoadErrorProperty.GetValue(panel);

    /// <inheritdoc cref="LoadError(object)"/>
    public static string? LoadError<TPanel>(IRenderedComponent<TPanel> panel) where TPanel : Microsoft.AspNetCore.Components.IComponent
        => LoadError(panel.Instance);

    /// <summary>Marks the panel as having read what the test placed on it: no failure, and loaded.</summary>
    public static void MarkLoaded(object panel)
    {
        LoadErrorProperty.SetValue(panel, null);
        LoadedProperty.SetValue(panel, true);
    }
}
