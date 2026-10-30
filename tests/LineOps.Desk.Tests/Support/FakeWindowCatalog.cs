using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests.Support;

/// <summary>
/// A catalogue with nothing of any application in it: one window that opens on its own and leads
/// an empty desk, two that are about something and so cannot, and one workspace. It is the
/// smallest catalogue that has each kind the desk treats differently, so a Desk test that needs
/// "a window" or "a subject window" names one of these rather than an application's.
/// </summary>
public sealed class FakeWindowCatalog : IWindowCatalog
{
    public const string One = "one";
    public const string Two = "two";
    public const string Three = "three";

    /// <summary>The view every fake window hosts. It draws nothing; the desk's chrome is what is under test.</summary>
    public sealed class Blank : ComponentBase;

    public IReadOnlyList<WindowDefinition> All { get; } =
    [
        new()
        {
            Key = One, Title = "One", Icon = "dashboard", ComponentType = typeof(Blank),
            Description = "A window that opens on its own.",
        },
        new()
        {
            Key = Two, Title = "Two", Icon = "article", ComponentType = typeof(Blank),
            Singleton = false, RequiresSubject = true, ReachedBy = "Open it from One.",
            Description = "A window about one thing.",
        },
        new()
        {
            Key = Three, Title = "Three", Icon = "article", ComponentType = typeof(Blank),
            Singleton = false, RequiresSubject = true, ReachedBy = "Open it from Two.",
            Description = "Another window about one thing.",
        },
    ];

    public IReadOnlyList<Workspace> Workspaces { get; } =
        [new Workspace("Start", "The one window that opens on its own.", [One])];

    public string DefaultPrimary => One;

    public WindowDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
