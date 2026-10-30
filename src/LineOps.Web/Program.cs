using LineOps.Data;
using LineOps.Desk;
using LineOps.Ingestion;
using LineOps.Observability;
using LineOps.Reliability;
using LineOps.Web.Access;
using LineOps.Web.Components;
using LineOps.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Local overrides, last so they win. This is where provider API keys live: the file is
// gitignored, which the environment-named ones (appsettings.Development.json) are not, so a key
// put in the obvious place would otherwise be committed. Optional, so a clone with no keys still
// starts — it just has no metered source registered, which the pull menu already states.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The desk — window manager, toasts, confirms, theme, and MudBlazor's services under them — is
// its own project and knows nothing about sports. It is told what this application is called and
// what its palette can find, reads its windows from the catalogue registered beneath it, and
// tells time in the zone the leagues keep — the same one every panel formats in (DisplayTime).
// See src/LineOps.Desk/DeskHost.cs for what each seam is.
builder.Services.AddDesk(
    new DeskBrand("LINE", "OPS", "sports-data ingestion & analytics operations", "Go to a team, player, game or window"),
    new DeskClock(LineOps.Core.Analytics.LeagueClock.Zone, LineOps.Web.Components.DisplayTime.Label));
builder.Services.AddSingleton<IWindowCatalog, LineOps.Web.Windowing.AppWindowCatalog>();

// Open windows refresh when the data under them changes, whichever process wrote it.
builder.Services.AddHostedService<LineOps.Web.Services.DataChangeListener>();

// What the command palette (Ctrl+K) finds beyond the desk's own windows: teams, players, games.
builder.Services.AddScoped<LineOps.Desk.Windowing.IDeskSearch, LineOps.Web.Services.AppDeskSearch>();

// The slip: one per desk session. Scoped is circuit-scoped under Interactive Server, so every
// window on this desk shares it and it goes with the circuit (spec §3.1).
builder.Services.AddScoped<LineOps.Web.Services.WagerSlip>();
builder.Services.AddScoped<LineOps.Web.Services.IWagerData, LineOps.Web.Services.WagerData>();

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

// Who may reach the desk and what they may do there (ADR 0018), and what changes when a reverse
// proxy stands in front of it (ADR 0019). Both register nothing in their defaults — open access,
// no proxy — so a laptop runs exactly as it always has.
builder.Services.AddLineOpsAccess(builder.Configuration);
builder.Services.AddEdgeHardening(builder.Configuration);

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

app.UseEdgeHardening();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Behind a proxy, the proxy owns port 80 and the redirect; the host has no HTTPS port to send
// anyone to.
if (!app.Configuration.BehindProxy())
    app.UseHttpsRedirection();

var gated = app.Configuration.IsGated();
if (gated)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
}

app.UseAntiforgery();

if (gated)
    app.MapLineOpsAccess();

// Liveness answers for the process only, so a database outage never gets the container killed
// and restarted into the same outage. Readiness is what compose and any orchestrator gate on.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ObservabilityServiceCollectionExtensions.ReadyTag)
}).AllowAnonymous();

// Stylesheets, scripts and the icon are not the desk; the sign-in page and the reconnect
// overlay need them before anyone has signed in.
app.MapStaticAssets().AllowAnonymous();

// Global Interactive Server render mode: MudBlazor does not support static server
// rendering, so interactivity is declared once at the root rather than per component.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
