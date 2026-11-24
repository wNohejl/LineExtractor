using XLines.Data.Placement;
using Microsoft.Extensions.Configuration;

namespace XLines.Ingestion.Configuration;

/// <summary>
/// Whether this host may run the unattended scheduler and the evaluator against the database it
/// is pointed at.
///
/// <para>
/// A desk on a second machine can point at the first machine's Postgres (ADR 0023). That is one
/// dataset, but only if it is also one scheduler: the web host schedules and evaluates by default,
/// so a laptop that took the desktop's connection string and nothing else would start a second
/// scheduler polling the same metered key and a second gamecast feed writing every play twice.
/// Nothing would throw. The credits would simply go, which is the expensive direction to fail
/// silently in (ADR 0014). So a host whose database is not its own refuses to start while it
/// would schedule or evaluate, and says what to set.
/// </para>
///
/// <para>
/// "Its own" is decided from the connection string alone: loopback, <c>localhost</c>, the compose
/// service name <c>postgres</c>, or a Unix socket. Anything else — a tailnet name, a 100.x
/// address, a LAN address, this machine's own hostname — is treated as remote. That errs towards
/// refusing, and <see cref="AllowRemoteSchedulingKey"/> is the deliberate way past it for the one
/// machine that is meant to be the scheduler while reaching its database by another name.
/// </para>
/// </summary>
public static class SchedulerPlacement
{
    /// <summary>The override: this host schedules and evaluates even though the database is remote.</summary>
    public const string AllowRemoteSchedulingKey = "Ingestion:AllowRemoteScheduling";

    public const string HostSchedulerKey = "Ingestion:HostScheduler";
    public const string HostEvaluatorKey = "Reliability:HostEvaluator";

    /// <summary>The compose service name: the database as the web and worker containers reach it.</summary>
    public const string ComposeServiceHost = DatabasePlacement.ComposeServiceHost;

    /// <summary>
    /// True when every host the connection string names is this machine's own database. The
    /// classifier is <see cref="DatabasePlacement"/>, in the data layer, so this guard and the
    /// initializer's migration guard cannot disagree about the same string.
    /// </summary>
    public static bool IsOwnDatabase(string? connectionString) => DatabasePlacement.IsOwnDatabase(connectionString);

    /// <summary>True for a single host entry (optionally with a port) that is this machine's own.</summary>
    public static bool IsOwnHost(string host) => DatabasePlacement.IsOwnHost(host);

    /// <summary>
    /// Why this host may not start, or null when it may. Pure, so the decision is tested without
    /// a host or a database. <paramref name="remedy"/> is what this host should be told to switch
    /// off; the web host's two switches when not given.
    /// </summary>
    public static string? Refusal(string? connectionString, bool hostsScheduler, bool hostsEvaluator, bool allowRemote,
        string? remedy = null)
    {
        if (allowRemote || (!hostsScheduler && !hostsEvaluator) || IsOwnDatabase(connectionString))
            return null;

        var roles = (hostsScheduler, hostsEvaluator) switch
        {
            (true, true) => $"the ingestion scheduler ({HostSchedulerKey}) and the evaluator ({HostEvaluatorKey})",
            (true, false) => $"the ingestion scheduler ({HostSchedulerKey})",
            _ => $"the evaluator ({HostEvaluatorKey})",
        };

        return $"Refusing to start: the database at '{DescribeHost(connectionString)}' is not this machine's own, "
            + $"and this host would run {roles} against it. Another machine already schedules for that database, and a "
            + "second scheduler would spend the same odds credits and write every play twice. "
            + (remedy ?? $"Set {HostSchedulerKey}=false and {HostEvaluatorKey}=false on this machine "
                + "(scripts/connect-remote-db.ps1 does both)")
            + ", or, if this machine is deliberately the one scheduler for "
            + $"that database, set {AllowRemoteSchedulingKey}=true.";
    }

    /// <summary>
    /// Reads the decision's inputs from configuration and throws when the host may not start.
    /// <paramref name="hostsScheduler"/> and <paramref name="hostsEvaluator"/> are what this host
    /// is about to register: the web host reads its switches, the Worker schedules whenever it is
    /// not running a single named job.
    /// </summary>
    public static void EnsureAllowed(IConfiguration configuration, string connectionStringName,
        bool hostsScheduler, bool hostsEvaluator)
    {
        var refusal = Refusal(
            configuration.GetConnectionString(connectionStringName),
            hostsScheduler,
            hostsEvaluator,
            configuration.GetValue(AllowRemoteSchedulingKey, false));

        if (refusal is not null)
            throw new InvalidOperationException(refusal);
    }

    private static string DescribeHost(string? connectionString) => DatabasePlacement.DescribeHost(connectionString);
}
