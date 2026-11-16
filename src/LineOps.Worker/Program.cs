using LineOps.Data;
using LineOps.Ingestion;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
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

// `--job espn:range --sport nfl --from 2024-09-01 --to 2024-09-30 [--refresh true]` pulls a league's
// games over those days, with each final's box score and close (RangePull), and prints what it did.
// The arguments are checked before the host is built, so a typo costs nothing and says what to fix.
RangePullRequest? range = null;

if (job == IngestionJobs.EspnRange
    && !RangePullRequest.TryParse(builder.Configuration["sport"], builder.Configuration["from"],
        builder.Configuration["to"], builder.Configuration["refresh"], out range, out var rangeError))
{
    Console.Error.WriteLine(rangeError);
    Console.Error.WriteLine(RangePull.Usage);
    return 2;
}

if (job is null)
{
    // The Worker is a scheduler whenever it is not running one named job, so it takes the web
    // host's guard (ADR 0023): against another machine's database it schedules only when told to
    // with Ingestion:AllowRemoteScheduling. A single `--job` is a deliberate press, like the
    // desk's pull menu, and is not refused.
    var refusal = SchedulerPlacement.Refusal(
        builder.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName),
        hostsScheduler: true, hostsEvaluator: true,
        allowRemote: builder.Configuration.GetValue(SchedulerPlacement.AllowRemoteSchedulingKey, false),
        remedy: "Run one named job with --job instead of the schedule");

    if (refusal is not null)
    {
        Console.Error.WriteLine(refusal);
        return 3;
    }

    builder.Services.AddLineOpsIngestionScheduler();
    builder.Services.AddLineOpsReliabilityEvaluator();
}

var host = builder.Build();

// Every run initialises, a single `--job` included, so a press from this machine against another
// machine's database follows the same rule as a desk (ADR 0023): no migration there, and a
// refusal when the schema is not the one this code was built for.
await using (var scope = host.Services.CreateAsyncScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    try
    {
        await initialiser.InitialiseAsync();
    }
    catch (LineOps.Data.Placement.RemoteDatabaseRefusedException refused)
    {
        Console.Error.WriteLine(refused.Message);
        return 4;
    }
}

if (range is not null)
{
    var summary = await host.Services.GetRequiredService<RangePull>().RunAsync(
        range, new ConsoleProgress<RangePullProgress>(p =>
            Console.WriteLine($"{p.Sport} {p.Day:yyyy-MM-dd} ({p.Done + 1} of {p.Of})")),
        CancellationToken.None);

    var ranged = summary.ToOutcome();
    Console.WriteLine($"{ranged.Key}: {(ranged.Succeeded ? "succeeded" : "failed")}. {ranged.Detail}");

    return ranged.Succeeded ? 0 : 1;
}

if (job is not null)
{
    var jobs = host.Services.GetRequiredService<IngestionJobs>();
    JobOutcome outcome;

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

/// <summary>Progress written as it is reported, so the days print in the order they are walked.</summary>
internal sealed class ConsoleProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
