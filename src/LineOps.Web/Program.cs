using LineOps.Data;
using LineOps.Ingestion;
using LineOps.Observability;
using LineOps.Reliability;
using LineOps.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Local overrides, last so they win. This is where provider API keys live: the file is
// gitignored, which the environment-named ones (appsettings.Development.json) are not, so a key
// put in the obvious place would otherwise be committed. Optional, so a clone with no keys still
// starts — it just has no metered source registered, which the pull menu already states.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// One desk per circuit: window layout is per-session state, not per-request.
builder.Services.AddScoped<LineOps.Web.Windowing.WindowManager>();

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
