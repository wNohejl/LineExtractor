using LineOps.Data;
using LineOps.Ingestion;
using LineOps.Observability;
using LineOps.Reliability;
using LineOps.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Local overrides, last so they win. This is where provider API keys live: the file is
// gitignored, which the environment-named ones (appsettings.Development.json) are not, so a key
// put in the obvious place would otherwise be committed. Optional, so a clone with no keys still
// starts — it just has no metered source registered, which the pull menu already states.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(options =>
{
    // Toasts land bottom-right, clear of the side rail and of the pulse strip in the header.
    // Newest on top so a burst reads in the order it happened from the corner inwards.
    options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    options.SnackbarConfiguration.NewestOnTop = true;
    // Duplicates are allowed, against MudBlazor's default. A desk notice is derived from an
    // outcome, so pressing Evaluate twice on an unchanged platform produces the same string
    // twice — and suppressing the second one recreates the "did that actually run?" doubt the
    // toast exists to remove.
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
});

// One desk per circuit: window layout is per-session state, not per-request.
builder.Services.AddScoped<LineOps.Web.Windowing.WindowManager>();

// The toast seam. Scoped because ISnackbar is: a notice belongs to the circuit that raised
// it. Panels take DeskToasts, never ISnackbar — see Components/Desk/DeskToasts.cs.
builder.Services.AddScoped<LineOps.Web.Components.Desk.DeskToasts>();

// The confirm seam, beside the toast one. Scoped for the same reason: the dialog stack it
// drives is per-circuit. Call sites take IDeskAlerts, never IDialogService, so a guarded
// action stays one await — see Components/Desk/DeskAlerts.cs.
builder.Services.AddScoped<LineOps.Web.Components.Desk.IDeskAlerts, LineOps.Web.Components.Desk.DeskAlerts>();

// Which desk is showing. Scoped for the third time and the same reason: it writes to one
// circuit's <html> and reads one browser's localStorage, so a singleton would hand every
// operator on the server whoever chose last. See Theming/ThemeService.cs.
builder.Services.AddScoped<LineOps.Web.Theming.ThemeService>();

// Persist Data Protection keys outside the container when a path is configured. Without this
// a replaced container generates fresh keys, which silently invalidates every live Blazor
// circuit and antiforgery token. Unset (the default when running from the SDK) keeps the
// framework's own per-user location.
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("LineOps");
}

builder.Services.AddLineOpsData(builder.Configuration);
builder.Services.AddLineOpsIngestion(builder.Configuration);
builder.Services.AddLineOpsReliability(builder.Configuration);
builder.Services.AddLineOpsObservability(builder.Configuration);
builder.Services.AddLineOpsHealthChecks();

// Single-process deployment: the web app also hosts the schedule and the evaluator, so a
// personal instance is one `dotnet run`. Both are opt-in registrations, so splitting the
// worker into its own process later is a startup change rather than a refactor.
if (builder.Configuration.GetValue("Ingestion:HostScheduler", true))
    builder.Services.AddLineOpsIngestionScheduler();

if (builder.Configuration.GetValue("Reliability:HostEvaluator", true))
    builder.Services.AddLineOpsReliabilityEvaluator();

var app = builder.Build();

// Migrate, ensure partitions exist ahead of the clock, and seed reference rows.
// Doing this at startup keeps a cold clone one `docker compose up` away from working.
await using (var scope = app.Services.CreateAsyncScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initialiser.InitialiseAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Liveness answers for the process only, so a database outage never gets the container killed
// and restarted into the same outage. Readiness is what compose and any orchestrator gate on.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ObservabilityServiceCollectionExtensions.ReadyTag)
}).AllowAnonymous();

app.MapStaticAssets();

// Global Interactive Server render mode: MudBlazor does not support static server
// rendering, so interactivity is declared once at the root rather than per component.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
