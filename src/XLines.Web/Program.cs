using XLines.Data;
using XLines.Desk;
using XLines.Desk.Hosting;
using XLines.Ingestion;
using XLines.Ingestion.Configuration;
using XLines.Observability;
using XLines.Reliability;
using XLines.Web.Components;
using XLines.Web.Services;
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
// See src/XLines.Desk/DeskHost.cs for what each seam is.
builder.Services.AddDesk(
    // The product is XLines (ADR 0031). The layout key keeps the name it was saved under, so a
    // desk laid out as XLines opens as it was left.
    new DeskBrand("X", "LINES", "sports-data ingestion & analytics operations", "Go to a team, player, game or window") { StorageKey = "lineops.desk" },
    new DeskClock(XLines.Core.Analytics.LeagueClock.Zone, XLines.Web.Components.DisplayTime.Label));
builder.Services.AddSingleton<IWindowCatalog, XLines.Web.Windowing.AppWindowCatalog>();

// Open windows refresh when the data under them changes, whichever process wrote it.
builder.Services.AddHostedService<XLines.Web.Services.DataChangeListener>();

// What the command palette (Ctrl+K) finds beyond the desk's own windows: teams, players, games.
builder.Services.AddScoped<XLines.Desk.Windowing.IDeskSearch, XLines.Web.Services.AppDeskSearch>();

// What the Board's Pull data menu offers: the named ingestion jobs the scheduler runs, each with
// its cost, on the desk's job menu.
builder.Services.AddScoped<XLines.Desk.IDeskJobs, XLines.Web.Services.IngestionDeskJobs>();

// The slip: one per desk session. Scoped is circuit-scoped under Interactive Server, so every
// window on this desk shares it and it goes with the circuit (spec §3.1).
builder.Services.AddScoped<XLines.Web.Services.WagerSlip>();
builder.Services.AddScoped<XLines.Web.Services.IWagerData, XLines.Web.Services.WagerData>();
builder.Services.AddScoped<XLines.Web.Services.ILineMovementData, XLines.Web.Services.LineMovementData>();
builder.Services.AddScoped<XLines.Web.Services.IMatchupData, XLines.Web.Services.MatchupData>();
builder.Services.AddScoped<XLines.Web.Services.IGameWindowData, XLines.Web.Services.GameWindowData>();
builder.Services.AddScoped<XLines.Web.Services.IGamecastData, XLines.Web.Services.GamecastData>();

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
// no proxy — so a laptop runs exactly as it always has. Both live in XLines.Desk.Hosting. The
// cookie keeps the name it had before the gate moved there, so every existing sign-in survives.
builder.Services.AddDeskAccess(builder.Configuration, o => o.CookieName = "lineops.access");
// In Development the tooling's own sockets (dotnet watch's browser refresh, Browser Link) are let
// through to localhost; everywhere else the policy is the Desk's default, and nothing leaves.
builder.Services.AddDeskEdge(builder.Configuration, edge =>
{
    if (builder.Environment.IsDevelopment())
        edge.ContentSecurityPolicy = DeskEdge.DevelopmentContentSecurityPolicy;

    // The one thing fetched from another origin: a club's crest, from the host ESPN serves
    // them on (TeamLogos). An image only; script, style and data still come from here alone.
    edge.ExtraImageSources.Add(XLines.Web.Components.Desk.TeamLogos.Origin);
});

// What a visitor may read, by the licence of the source each row came from (ADR 0024). Local, the
// default, shows everyone everything; Shared keeps personal-use data (ESPN, MLB) to the operator.
builder.Services.AddDataSharing(builder.Configuration);

builder.Services.AddXLinesData(builder.Configuration);
builder.Services.AddXLinesIngestion(builder.Configuration);

// The Odds API key can be typed into Ops as well as set in configuration: checked for free
// against the provider, kept encrypted beside the Data Protection keys, used from the next pull.
builder.Services.AddRuntimeOddsKey();
builder.Services.AddHttpClient(DeskOddsKey.CheckClient, client =>
{
    client.BaseAddress = new Uri("https://api.the-odds-api.com/v4/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton(sp => new DeskOddsKey(
    sp.GetRequiredService<IDataProtectionProvider>(),
    sp.GetRequiredService<XLines.Ingestion.Services.RuntimeOddsKey>(),
    sp.GetRequiredService<XLines.Ingestion.Services.ReferenceReconciler>().ReconcileAsync,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<DeskOddsKey>>()));

builder.Services.AddXLinesReliability(builder.Configuration);
builder.Services.AddXLinesObservability(builder.Configuration);
builder.Services.AddXLinesHealthChecks();

// Single-process deployment: the web app also hosts the schedule and the evaluator, so a
// personal instance is one `dotnet run`. Both are opt-in registrations, so splitting the
// worker into its own process later is a startup change rather than a refactor.
var hostsScheduler = builder.Configuration.GetValue(SchedulerPlacement.HostSchedulerKey, true);
var hostsEvaluator = builder.Configuration.GetValue(SchedulerPlacement.HostEvaluatorKey, true);

// A desk pointed at another machine's database (ADR 0023) must not become that database's second
// scheduler by accident: it refuses to start until both switches are off, or the override is set.
SchedulerPlacement.EnsureAllowed(builder.Configuration, ServiceCollectionExtensions.ConnectionStringName,
    hostsScheduler, hostsEvaluator);

if (hostsScheduler)
    builder.Services.AddXLinesIngestionScheduler();

if (hostsEvaluator)
    builder.Services.AddXLinesReliabilityEvaluator();

var app = builder.Build();

// Before anything asks which sources are configured: the reference reconciler does at start.
app.Services.GetRequiredService<DeskOddsKey>().Load();

// Migrate, ensure partitions exist ahead of the clock, and seed reference rows.
// Doing this at startup keeps a cold clone one `docker compose up` away from working. Against
// another machine's database (ADR 0023) it migrates nothing: it refuses to start when the schema
// is not the one this code was built for, and says which machine should move.
await using (var scope = app.Services.CreateAsyncScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    try
    {
        await initialiser.InitialiseAsync();
    }
    catch (XLines.Data.Placement.RemoteDatabaseRefusedException refused)
    {
        Console.Error.WriteLine(refused.Message);
        Environment.ExitCode = 4;
        return;
    }
}

app.UseDeskEdge();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Behind a proxy, the proxy owns port 80 and the redirect; the host has no HTTPS port to send
// anyone to.
if (!app.Configuration.IsBehindProxy())
    app.UseHttpsRedirection();

// Gated only: authentication, authorization and the sign-in rate limit, then the sign-in page.
app.UseDeskAccess();

app.UseAntiforgery();

app.MapDeskAccess();

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
