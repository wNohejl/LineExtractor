using LineOps.Data;
using LineOps.Ingestion;
using LineOps.Observability;
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
builder.Services.AddLineOpsObservability(builder.Configuration);

// `--job espn:closing-lines` runs one named job and exits, without the schedule: the way to make
// a single pull — a backport, a settle — from a terminal, beside a desk that is already running.
// `--until-done true` runs it again for as long as it says more is owed (JobOutcome.More) and has
// not failed: `--job espn:gamecast:backport --until-done true` walks every batch of the backport.
var job = builder.Configuration["job"];
var untilDone = builder.Configuration.GetValue<bool>("until-done");

if (job is null)
{
    builder.Services.AddLineOpsIngestionScheduler();
    builder.Services.AddLineOpsReliabilityEvaluator();
}

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initialiser.InitialiseAsync();
}

if (job is not null)
{
    var jobs = host.Services.GetRequiredService<LineOps.Ingestion.Services.IngestionJobs>();
    LineOps.Ingestion.Services.JobOutcome outcome;

    do
    {
        outcome = await jobs.RunAsync(job);
        Console.WriteLine($"{outcome.Key}: {(outcome.Succeeded ? "succeeded" : "failed")}, {outcome.Rows:N0} rows, "
                          + $"{outcome.Failures} failures. {outcome.Detail}");
    }
    while (untilDone && outcome.Succeeded && outcome.More);

    return outcome.Succeeded ? 0 : 1;
}

await host.RunAsync();
return 0;
