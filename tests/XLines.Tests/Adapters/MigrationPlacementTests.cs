using XLines.Data.Placement;
using XLines.Ingestion.Configuration;

namespace XLines.Tests.Adapters;

/// <summary>
/// One database, one migrator (ADR 0023, amendment of 2026-11-15).
///
/// A laptop's desk pointed at the desktop's database must not migrate it: a laptop on a newer
/// branch would leave the desktop's desk running older code against a newer schema. These pin
/// every branch of the decision: a local database migrates as it always has; a remote one on the
/// same schema starts without touching it; a remote one this code is newer or older than refuses,
/// and says which machine should move; and the override is the deliberate way past it.
/// </summary>
public class MigrationPlacementTests
{
    private static readonly string[] Known = ["20260101_Initial", "20260201_Odds", "20260301_Bouts"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThisMachinesOwnDatabaseMigratesWhateverItHolds(bool allowRemote)
    {
        // Pending and unknown alike: a local database is migrated exactly as before.
        var decision = MigrationPlacement.Decide(isOwnDatabase: true, applied: ["20260101_Initial", "20251201_Gone"],
            known: Known, allowRemoteMigration: allowRemote);

        Assert.Equal(MigrationAction.Migrate, decision.Action);
        Assert.Null(decision.Message);
    }

    [Fact]
    public void ARemoteDatabaseOnTheSameSchemaIsUsedAsItIs()
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false, applied: Known.Reverse(), known: Known,
            allowRemoteMigration: false);

        Assert.Equal(MigrationAction.Proceed, decision.Action);
        Assert.Null(decision.Message);
    }

    [Fact]
    public void ARemoteDatabaseThisCodeIsNewerThanIsRefusedAndNamesThePendingMigrations()
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false, applied: ["20260101_Initial"], known: Known,
            allowRemoteMigration: false, host: "desktop:5433");

        Assert.Equal(MigrationAction.Refuse, decision.Action);
        Assert.Contains("'desktop:5433'", decision.Message);
        Assert.Contains("2 migrations the database has not applied (20260201_Odds, 20260301_Bouts)", decision.Message);
        Assert.Contains("newer than the database", decision.Message);
        Assert.Contains("machine that owns the database", decision.Message);
        Assert.Contains(MigrationPlacement.AllowRemoteMigrationKey + "=true", decision.Message);
    }

    [Fact]
    public void AnEmptyRemoteDatabaseIsRefusedRatherThanMigratedFromHere()
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false, applied: [], known: Known,
            allowRemoteMigration: false);

        Assert.Equal(MigrationAction.Refuse, decision.Action);
        Assert.Contains("3 migrations the database has not applied", decision.Message);
    }

    [Fact]
    public void ARemoteDatabaseThisCodeIsOlderThanIsRefusedAndSaysToPull()
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false, applied: [.. Known, "20260401_Fighters"],
            known: Known, allowRemoteMigration: false);

        Assert.Equal(MigrationAction.Refuse, decision.Action);
        Assert.Contains("1 migration this code does not know (20260401_Fighters)", decision.Message);
        Assert.Contains("older than the database", decision.Message);
        Assert.Contains("Pull the newer commit", decision.Message);
        // Migrating cannot fix an older checkout, so the override is not offered.
        Assert.DoesNotContain(MigrationPlacement.AllowRemoteMigrationKey, decision.Message);
    }

    [Fact]
    public void ARemoteDatabaseOnADivergedBranchIsRefusedAndNamesBothSides()
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false,
            applied: ["20260101_Initial", "20260201_Odds", "20260401_Fighters"], known: Known,
            allowRemoteMigration: false);

        Assert.Equal(MigrationAction.Refuse, decision.Action);
        Assert.Contains("(20260401_Fighters)", decision.Message);
        Assert.Contains("(20260301_Bouts)", decision.Message);
        Assert.Contains("connect-remote-db.ps1 -Local", decision.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("20260101_Initial")]
    [InlineData("20260101_Initial,20260201_Odds,20260301_Bouts,20260401_Fighters")]
    public void TheOverrideMigratesARemoteDatabaseDeliberately(string applied)
    {
        var decision = MigrationPlacement.Decide(isOwnDatabase: false,
            applied: applied.Split(',', StringSplitOptions.RemoveEmptyEntries), known: Known,
            allowRemoteMigration: true);

        Assert.Equal(MigrationAction.Migrate, decision.Action);
    }

    [Theory]
    [InlineData("Host=localhost;Port=5433")]
    [InlineData("Host=127.0.0.1;Port=5433")]
    [InlineData("Host=postgres;Port=5432")]
    [InlineData("Host=desktop.tailnet.ts.net;Port=5433")]
    [InlineData("Host=100.64.0.1;Port=5433")]
    [InlineData("Host=localhost,192.168.1.20;Port=5433")]
    public void TheMigrationGuardAndTheSchedulerGuardAgreeOnWhatIsRemote(string connectionString)
        => Assert.Equal(SchedulerPlacement.IsOwnDatabase(connectionString),
            DatabasePlacement.IsOwnDatabase(connectionString));
}
