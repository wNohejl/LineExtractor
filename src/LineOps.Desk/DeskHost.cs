using LineOps.Desk.Primitives;
using LineOps.Desk.Theming;
using LineOps.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk;

/// <summary>
/// What an application tells the desk about itself: the wordmark, split where the accent
/// falls, and the line under it on an empty desk.
/// </summary>
public sealed record DeskBrand(string Wordmark, string Emphasis, string Tagline);

/// <summary>
/// Every window an application can open, and the arrangements it names.
///
/// <para>
/// The desk reads windows from here and nowhere else: the strip draws one key per entry,
/// the manager opens by key, workspaces are lists of keys. An application implements this
/// once, usually over a static list, and registers it as a singleton. See
/// <see cref="WindowDefinition"/> for what an entry says.
/// </para>
/// </summary>
public interface IWindowCatalog
{
    IReadOnlyList<WindowDefinition> All { get; }

    IReadOnlyList<Workspace> Workspaces { get; }

    /// <summary>The window an empty desk opens, and that leads the row by default.</summary>
    string DefaultPrimary { get; }

    WindowDefinition? Find(string key);
}

public static class DeskServiceCollectionExtensions
{
    /// <summary>
    /// Registers the desk. The application registers its own <see cref="IWindowCatalog"/>.
    ///
    /// <para>
    /// Everything here is scoped, because everything here is per circuit: the window layout
    /// is one session's state, a toast belongs to the circuit that raised it, the dialog
    /// stack is per circuit, and the theme writes to one circuit's <c>&lt;html&gt;</c>. A
    /// singleton would hand every operator on the server whoever acted last.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDesk(this IServiceCollection services, DeskBrand brand)
    {
        services.AddSingleton(brand);
        services.AddScoped<WindowManager>();
        services.AddScoped<DeskToasts>();
        services.AddScoped<IDeskAlerts, DeskAlerts>();
        services.AddScoped<ThemeService>();
        return services;
    }
}
