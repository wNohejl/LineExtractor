using System.Net;
using Npgsql;

namespace XLines.Data.Placement;

/// <summary>
/// Whether a connection string names this machine's own database (ADR 0023).
///
/// <para>
/// Two guards ask. The scheduler guard (<c>SchedulerPlacement</c>, in Ingestion) refuses a second
/// scheduler against another machine's database; the initializer (<see cref="MigrationPlacement"/>)
/// refuses to migrate it. Both must give the same answer for the same string, so the classifier
/// lives here, in the data layer both hosts already reference, and the scheduler guard calls it.
/// </para>
///
/// <para>
/// "Its own" is decided from the connection string alone: loopback, <c>localhost</c>, the compose
/// service name <c>postgres</c>, or a Unix socket. Anything else — a tailnet name, a 100.x
/// address, a LAN address, this machine's own hostname — is treated as remote. That errs towards
/// refusing; each guard has its own deliberate override.
/// </para>
/// </summary>
public static class DatabasePlacement
{
    /// <summary>The compose service name: the database as the web and worker containers reach it.</summary>
    public const string ComposeServiceHost = "postgres";

    /// <summary>
    /// True when every host the connection string names is this machine's own database. A string
    /// that cannot be read, or names no host, is not judged here: the data layer refuses it on its
    /// own terms, and the guards' job is the remote case only.
    /// </summary>
    public static bool IsOwnDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return true;

        string? hosts;
        try
        {
            hosts = new NpgsqlConnectionStringBuilder(connectionString).Host;
        }
        catch (ArgumentException)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(hosts))
            return true;

        // Npgsql takes a comma-separated list for failover; one remote entry is enough to make it
        // a database another machine could also be using.
        return hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .All(IsOwnHost);
    }

    /// <summary>True for a single host entry (optionally with a port) that is this machine's own.</summary>
    public static bool IsOwnHost(string host)
    {
        var name = StripPort(host.Trim());

        if (name.Length == 0)
            return true;

        // A Unix-domain socket directory is on this machine by definition.
        if (name.StartsWith('/'))
            return true;

        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals(ComposeServiceHost, StringComparison.OrdinalIgnoreCase))
            return true;

        // 127.0.0.0/8 and ::1, however written. A name that is not an address is remote.
        return IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>"host:port" for a message, never the password; "(unreadable)" when it cannot be read.</summary>
    public static string DescribeHost(string? connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"{builder.Host}:{builder.Port}";
        }
        catch (ArgumentException)
        {
            return "(unreadable)";
        }
    }

    /// <summary>"host:5433" → "host", "[::1]:5433" → "::1", "::1" → "::1".</summary>
    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close > 0 ? host[1..close] : host.Trim('[', ']');
        }

        // More than one colon is a bare IPv6 address, which carries no port in this form.
        var colon = host.IndexOf(':');
        return colon >= 0 && colon == host.LastIndexOf(':') ? host[..colon] : host;
    }
}
