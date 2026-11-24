using XLines.Ingestion.Configuration;
using Microsoft.Extensions.Configuration;

namespace XLines.Tests.Adapters;

/// <summary>
/// One database, one scheduler (ADR 0023).
///
/// A laptop's desk can point at the desktop's Postgres over the tailnet. The web host schedules
/// and evaluates by default, so a connection string changed on its own would quietly start a
/// second spender of the same odds credits. These pin both halves of the guard: which hosts count
/// as this machine's own, and that a remote one refuses to start until the switches are off or the
/// override is given by name.
/// </summary>
public class SchedulerPlacementTests
{
    private const string Rest = "Port=5433;Database=lineops;Username=lineops;Password=x";

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("postgres")]
    [InlineData("localhost:5433")]
    [InlineData("/var/run/postgresql")]
    public void ThisMachinesOwnDatabase(string host)
        => Assert.True(SchedulerPlacement.IsOwnDatabase($"Host={host};{Rest}"));

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("desktop")]
    [InlineData("desktop.tail1234.ts.net")]
    [InlineData("192.168.1.20")]
    [InlineData("fd7a:115c:a1e0::1")]
    [InlineData("ep-cool-name-123456.us-east-2.aws.neon.tech")]
    [InlineData("localhost.example.com")]
    [InlineData("postgres.example.com")]
    public void AnotherMachinesDatabase(string host)
        => Assert.False(SchedulerPlacement.IsOwnDatabase($"Host={host};{Rest}"));

    [Fact]
    public void OneRemoteHostInAFailoverListMakesItRemote()
    {
        Assert.False(SchedulerPlacement.IsOwnDatabase($"Host=localhost,100.64.0.1;{Rest}"));
        Assert.True(SchedulerPlacement.IsOwnDatabase($"Host=localhost,127.0.0.1;{Rest}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Database=lineops")]
    public void AStringWithNoHostIsLeftToTheDataLayer(string? connectionString)
        => Assert.True(SchedulerPlacement.IsOwnDatabase(connectionString));

    [Fact]
    public void ALocalDatabaseSchedulesAsItAlwaysHas()
        => Assert.Null(SchedulerPlacement.Refusal($"Host=localhost;{Rest}", true, true, allowRemote: false));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ARemoteDatabaseRefusesASchedulerOrEvaluator(bool scheduler, bool evaluator)
    {
        var refusal = SchedulerPlacement.Refusal($"Host=desktop;{Rest}", scheduler, evaluator, allowRemote: false);

        Assert.NotNull(refusal);
        // The message is the fix: it names the host, the switch that is on, and the override.
        Assert.Contains("desktop:5433", refusal);
        Assert.Contains(SchedulerPlacement.AllowRemoteSchedulingKey + "=true", refusal);
        Assert.Equal(scheduler, refusal.Contains("ingestion scheduler (" + SchedulerPlacement.HostSchedulerKey + ")"));
        Assert.Equal(evaluator, refusal.Contains("evaluator (" + SchedulerPlacement.HostEvaluatorKey + ")"));
        Assert.DoesNotContain("Password", refusal);
    }

    [Fact]
    public void TheWorkerIsToldItsOwnWayOut()
    {
        // The Worker has no HostScheduler switch: it schedules unless it is running one job.
        var refusal = SchedulerPlacement.Refusal($"Host=desktop;{Rest}", true, true, allowRemote: false,
            remedy: "Run one named job with --job instead of the schedule");

        Assert.NotNull(refusal);
        Assert.Contains("--job", refusal);
        Assert.DoesNotContain("connect-remote-db.ps1", refusal);
        Assert.Contains(SchedulerPlacement.AllowRemoteSchedulingKey + "=true", refusal);
    }

    [Fact]
    public void ARemoteDatabaseIsFineForADeskThatNeitherSchedulesNorEvaluates()
        => Assert.Null(SchedulerPlacement.Refusal($"Host=desktop;{Rest}", false, false, allowRemote: false));

    [Fact]
    public void TheOverrideLetsTheOneSchedulerReachItsDatabaseByAnotherName()
        => Assert.Null(SchedulerPlacement.Refusal($"Host=desktop;{Rest}", true, true, allowRemote: true));

    [Fact]
    public void ConfigurationWithOnlyAConnectionStringIsRefused()
    {
        // The accident the guard exists for: the connection string changed, nothing else did.
        var configuration = Build(new() { ["ConnectionStrings:LineOps"] = $"Host=100.64.0.1;{Rest}" });

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            SchedulerPlacement.EnsureAllowed(configuration, "LineOps", hostsScheduler: true, hostsEvaluator: true));
        Assert.Contains("100.64.0.1", thrown.Message);
    }

    [Fact]
    public void ConfigurationWithTheOverrideStarts()
    {
        var configuration = Build(new()
        {
            ["ConnectionStrings:LineOps"] = $"Host=100.64.0.1;{Rest}",
            [SchedulerPlacement.AllowRemoteSchedulingKey] = "true",
        });

        SchedulerPlacement.EnsureAllowed(configuration, "LineOps", hostsScheduler: true, hostsEvaluator: true);
    }

    private static IConfiguration Build(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
