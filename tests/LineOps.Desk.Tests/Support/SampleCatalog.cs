using LineOps.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;
using TheDesk = LineOps.Desk.Windowing.Desk;

namespace LineOps.Desk.Tests.Support;

/// <summary>
/// A desk's worth of windows with no application behind them, for the tests of how the desk
/// behaves once there is something on it: enough catalogue windows to fill a row and evict, a wide
/// one and a narrow one, windows about one thing that follow their links in place, singletons that
/// take a subject, groups for the strip, two workspaces and the settings window. The widths and
/// shapes are the ones the desk behaviour was first written against, under neutral names.
/// </summary>
public sealed class SampleCatalog : IWindowCatalog
{
    // Catalogue windows: places the operator works from.
    public const string List = "list";
    public const string Chart = "chart";
    public const string Notes = "notes";
    public const string Health = "health";
    public const string Alerts = "alerts";
    public const string Jobs = "jobs";
    public const string Settings = TheDesk.SettingsKey;

    // Singletons about one thing: a follow-up opened from a row, re-aimed rather than doubled.
    public const string Details = "details";
    public const string Order = "order";

    // Windows about one thing that follow their links in place.
    public const string Item = "item";
    public const string Company = "company";
    public const string Person = "person";

    private static WindowDefinition Window(string key, string title, string group, int minWidth, double weight = 1) => new()
    {
        Key = key,
        Title = title,
        Icon = "",
        Group = group,
        ComponentType = typeof(FakeWindowCatalog.Blank),
        MinWidth = minWidth,
        DefaultWeight = weight,
    };

    public static readonly IReadOnlyList<WindowDefinition> Windows =
    [
        Window(List, "List", "Data", 680, 1.9),
        Window(Details, "Details", "Data", 420) with { RequiresSubject = true, ReachedBy = "Open a row on the List." },
        Window(Order, "Order", "Data", 340) with { RequiresSubject = true, OperatorOnly = true, ReachedBy = "Open a row on the List." },
        Window(Item, "Item", "Data", 560) with { RequiresSubject = true, Singleton = false, ReachedBy = "Follow a row from the List." },
        Window(Company, "Company", "Data", 380) with { RequiresSubject = true, Singleton = false, ReachedBy = "Follow a name from an item." },
        Window(Person, "Person", "Data", 440) with { RequiresSubject = true, Singleton = false, ReachedBy = "Follow a name from a company." },
        Window(Chart, "Chart", "Data", 380) with { Singleton = false },
        Window(Notes, "Notes", "Analytics", 460) with { OperatorOnly = true },
        Window(Health, "Health", "Operations", 420),
        Window(Alerts, "Alerts", "Operations", 400),
        Window(Jobs, "Jobs", "Operations", 440),
        Window(Settings, "Window manager", "System", 320),
    ];

    public static WindowDefinition Find(string key) => Windows.Single(d => d.Key == key);

    public IReadOnlyList<WindowDefinition> All => Windows;

    public IReadOnlyList<Workspace> Workspaces { get; } =
    [
        new("Triage", "Health first, then what broke, then the jobs behind it.", [Health, Alerts, Jobs]),
        new("Watch", "The list beside a chart.", [List, Chart]),
    ];

    public string DefaultPrimary => Health;

    WindowDefinition? IWindowCatalog.Find(string key) => Windows.FirstOrDefault(d => d.Key == key);
}

/// <summary>The bench with <see cref="SampleCatalog"/> as the desk's catalogue.</summary>
public abstract class SampleDeskTestContext : DeskTestContext
{
    protected SampleDeskTestContext() => Services.AddSingleton<IWindowCatalog, SampleCatalog>();
}
