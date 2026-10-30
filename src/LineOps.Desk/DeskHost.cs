using LineOps.Desk.Primitives;
using LineOps.Desk.Theming;
using LineOps.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using MudBlazor.Services;

namespace LineOps.Desk;

/// <summary>
/// What an application tells the desk about itself: the wordmark, split where the accent
/// falls, the line under it on an empty desk, and what the palette's empty field invites —
/// the things this application's search can find, which the desk cannot know.
/// </summary>
public sealed record DeskBrand(string Wordmark, string Emphasis, string Tagline, string SearchPlaceholder = "Search");

/// <summary>
/// The zone the desk tells time in, and the short name it labels that time with.
///
/// <para>
/// Blazor Server renders on the server, so <c>DateTime.Now</c> is the host's clock, not the
/// reader's — UTC in a container, whatever the desktop is set to under <c>dotnet run</c>. An
/// application that means a zone says which one here; the desk never guesses.
/// </para>
/// </summary>
public sealed record DeskClock(TimeZoneInfo Zone, string Label)
{
    /// <summary>Where "now" comes from. The system clock, unless a test says otherwise.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>What a desk shows when its application names no zone: UTC, and says so.</summary>
    public static readonly DeskClock Utc = new(TimeZoneInfo.Utc, "UTC");

    /// <summary>The wall-clock time in <see cref="Zone"/> at a moment.</summary>
    public DateTime At(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>The wall-clock time in <see cref="Zone"/> now.</summary>
    public DateTime Now() => At(Time.GetUtcNow());
}

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
    /// Registers the desk, and MudBlazor's services under it. The application registers its own
    /// <see cref="IWindowCatalog"/>, and does not call <c>AddMudServices</c> itself: <paramref name="mud"/>
    /// runs after the desk's defaults, so anything the application sets there wins.
    ///
    /// <para>
    /// Everything here is scoped, because everything here is per circuit: the window layout
    /// is one session's state, a toast belongs to the circuit that raised it, the dialog
    /// stack and the one open popover are per circuit, and the theme writes to one circuit's <c>&lt;html&gt;</c>. A
    /// singleton would hand every operator on the server whoever acted last.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDesk(
        this IServiceCollection services,
        DeskBrand brand,
        DeskClock? clock = null,
        Action<MudServicesConfiguration>? mud = null)
    {
        services.AddMudServices(options =>
        {
            // Toasts land bottom-right, clear of the side rail and of the pulse strip in the header.
            // Newest on top so a burst reads in the order it happened from the corner inwards.
            options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            options.SnackbarConfiguration.NewestOnTop = true;
            // Duplicates are allowed, against MudBlazor's default. A desk notice is derived from an
            // outcome, so pressing the same button twice on an unchanged state produces the same
            // string twice — and suppressing the second one recreates the "did that actually run?"
            // doubt the toast exists to remove.
            options.SnackbarConfiguration.PreventDuplicates = false;
            options.SnackbarConfiguration.MaxDisplayedSnackbars = 4;

            // Transient means transient: it goes on its own, and the operator never has to dismiss
            // one. The close button stays because a notice that lands over something being read
            // should be removable without waiting it out.
            options.SnackbarConfiguration.VisibleStateDuration = 4000;
            options.SnackbarConfiguration.ShowCloseIcon = true;

            // Material fades a snackbar in over half a second, which reads as the notice arriving
            // late. The desk's own transitions are ~120ms; match them.
            options.SnackbarConfiguration.ShowTransitionDuration = 120;
            options.SnackbarConfiguration.HideTransitionDuration = 200;

            // Last, so whatever the application says about MudBlazor overrides the desk's defaults.
            mud?.Invoke(options);
        });

        services.AddSingleton(brand);
        services.AddSingleton(clock ?? DeskClock.Utc);
        // One person on their own machine, who may do everything — unless the application
        // registers who is at the desk (IDeskAccess), before or after this call.
        services.TryAddSingleton<IDeskAccess>(OpenDeskAccess.Instance);
        services.AddScoped<WindowManager>();
        services.AddScoped<DeskFollowUps>();
        services.AddSingleton<DeskSignals>();
        services.AddScoped<DeskToasts>();
        services.AddScoped<IDeskAlerts, DeskAlerts>();
        services.AddScoped<IDeskSheets, DeskSheets>();
        services.AddScoped<DeskPopovers>();
        // The palette lists every provider; the desk's own commands are one of them.
        services.AddScoped<IDeskSearch, DeskCommands>();
        services.AddScoped<ThemeService>();
        return services;
    }
}
