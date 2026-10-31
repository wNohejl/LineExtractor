using LineOps.Desk;
using LineOps.Desk.Windowing;
using MudBlazor;

namespace DeskApp.Windows;

/// <summary>
/// Every window this desk can open, and the workspaces that arrange them. A new window is one
/// entry here: the strip, the palette and the manager all read this list.
/// </summary>
public sealed class AppWindowCatalog : IWindowCatalog
{
    public const string Welcome = "welcome";
    public const string Parts = "parts";

    public IReadOnlyList<WindowDefinition> All { get; } =
    [
        new()
        {
            Key = Welcome,
            Title = "Welcome",
            Icon = Icons.Material.Filled.WavingHand,
            Group = "Start",
            ComponentType = typeof(WelcomePanel),
            Description = "Where a new window begins: what this desk is, and how to add to it."
        },
        // The Desk's own gallery: every primitive, live, beside the window being built.
        new()
        {
            Key = Parts,
            Title = "Parts bin",
            Icon = Icons.Material.Filled.Widgets,
            Group = "System",
            ComponentType = typeof(PartsPanel),
            Description = "Every reusable control, live and side by side.",
            MinWidth = 380
        }
    ];

    public IReadOnlyList<Workspace> Workspaces { get; } =
    [
        new("Build a window", "The window under way beside the parts it is made from.", [Welcome, Parts])
    ];

    public string DefaultPrimary => Welcome;

    public WindowDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
