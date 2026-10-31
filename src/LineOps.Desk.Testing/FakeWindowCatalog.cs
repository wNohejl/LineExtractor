using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Testing;

/// <summary>
/// A catalogue with nothing of any application in it: one window that opens on its own and leads
/// an empty desk, two that are about something and so cannot, and one workspace. It is the
/// smallest catalogue that has each kind the desk treats differently, so a Desk test that needs
/// "a window" or "a subject window" names one of these rather than an application's.
///
/// <para>
/// A test that needs a larger desk — several windows to fill it, groups for the strip, a settings
/// window — hands its own definitions to the other constructor.
/// </para>
/// </summary>
public sealed class FakeWindowCatalog : IWindowCatalog
{
    public const string One = "one";
    public const string Two = "two";
    public const string Three = "three";

    /// <summary>The view every fake window hosts. It draws nothing; the desk's chrome is what is under test.</summary>
    public sealed class Blank : ComponentBase;

    /// <summary>The three windows and one workspace described above.</summary>
    public FakeWindowCatalog()
        : this(
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
            ],
            [new Workspace("Start", "The one window that opens on its own.", [One])],
            One)
    {
    }

    /// <summary>
    /// A catalogue of the given windows. The primary defaults to the first window that opens on its
    /// own.
    /// </summary>
    public FakeWindowCatalog(IReadOnlyList<WindowDefinition> windows, IReadOnlyList<Workspace>? workspaces = null,
        string? defaultPrimary = null)
    {
        All = windows;
        Workspaces = workspaces ?? [];
        DefaultPrimary = defaultPrimary ?? windows.First(w => !w.RequiresSubject).Key;
    }

    public IReadOnlyList<WindowDefinition> All { get; }

    public IReadOnlyList<Workspace> Workspaces { get; }

    public string DefaultPrimary { get; }

    public WindowDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
