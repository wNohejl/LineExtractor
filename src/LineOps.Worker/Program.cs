using LineOps.Data;
using LineOps.Ingestion;
using LineOps.Reliability;

// Standalone worker host. The web app can host the same schedule in-process for a personal
// deployment; this entry point exists so ingestion can be run and scaled on its own, with
// the UI reduced to a reader of what it produces.
var builder = Host.CreateApplicationBuilder(args);

// Local overrides, last so they win — see the note in the web host. Provider API keys live here
// because this file is gitignored and the environment-named ones are not.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddLineOpsData(builder.Configuration);
builder.Services.AddLineOpsIngestion(builder.Configuration);
builder.Services.AddLineOpsReliability(builder.Configuration);

builder.Services.AddLineOpsIngestionScheduler();
builder.Services.AddLineOpsReliabilityEvaluator();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initialiser.InitialiseAsync();
}

await host.RunAsync();
