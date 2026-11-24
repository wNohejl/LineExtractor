namespace XLines.Ingestion.Configuration;

/// <summary>
/// ESPN is opt-in, and this is what the desk, the Worker and the setup script say about it.
///
/// <para>
/// ESPN is the stats source — schedules, scores, box scores — through endpoints it neither
/// documents nor offers, and its terms prohibit automated access. So a clone calls nothing until
/// the person running it has read that and turned it on: <c>Ingestion:Espn:Enabled</c> is false
/// in code, in both hosts' appsettings.json and in the compose file (<c>ESPN_ENABLED</c>), and is
/// set to true only by the operator, in <c>.env</c> or <c>appsettings.Local.json</c>. The MLB
/// Stats API follows the same switch (see <c>AddXLinesIngestion</c>).
/// </para>
/// </summary>
public static class EspnOptIn
{
    /// <summary>The configuration key that turns ESPN on.</summary>
    public const string Switch = "Ingestion:Espn:Enabled";

    /// <summary>Why it is off until someone decides otherwise. Every fact here is load-bearing.</summary>
    public const string Notice =
        "ESPN's endpoints are unofficial and undocumented. ESPN's terms of use license its services for "
        + "personal, non-commercial use and prohibit automated access. Turning this on is your decision; "
        + "use it only to keep data in your own local database, never to host, redistribute or sell it.";

    /// <summary>How to turn it on for the Docker stack.</summary>
    public const string DockerHowTo =
        "Docker: set ESPN_ENABLED=true in .env, then start the stack again "
        + "(docker compose -f docker-compose.yml -f compose/dev.yml up -d).";

    /// <summary>How to turn it on for a host run.</summary>
    public const string HostHowTo =
        "dotnet run: set \"Ingestion\": { \"Espn\": { \"Enabled\": true } } in appsettings.Local.json "
        + "(or Ingestion__Espn__Enabled=true in the environment), then restart.";

    /// <summary>Whether a job key is one of ESPN's (<c>espn:slate</c>, <c>espn:range</c> …).</summary>
    public static bool IsEspnJob(string? key)
        => key is not null && key.StartsWith("espn:", StringComparison.Ordinal);

    /// <summary>
    /// The one-line refusal for an ESPN job on a host where ESPN is off: names the job and the
    /// switch, and asks for nothing from the network to say so.
    /// </summary>
    public static string Refusal(string job)
        => $"{job}: ESPN is off on this host ({Switch} is false). It is opt-in: turn it on with "
           + "ESPN_ENABLED=true in .env (Docker) or Ingestion__Espn__Enabled=true / appsettings.Local.json (dotnet run).";
}
