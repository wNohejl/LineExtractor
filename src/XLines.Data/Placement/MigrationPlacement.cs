namespace XLines.Data.Placement;

/// <summary>What the initializer does with the database it is pointed at.</summary>
public enum MigrationAction
{
    /// <summary>This machine's own database, or the override: migrate and seed, as always.</summary>
    Migrate,

    /// <summary>Another machine's database, on the schema this code was built for: start, change nothing.</summary>
    Proceed,

    /// <summary>Another machine's database, on a different schema: do not start.</summary>
    Refuse
}

/// <summary>The decision, and why when it is a refusal.</summary>
public sealed record MigrationDecision(MigrationAction Action, string? Message = null)
{
    public static readonly MigrationDecision Migrate = new(MigrationAction.Migrate);
    public static readonly MigrationDecision Proceed = new(MigrationAction.Proceed);
    public static MigrationDecision Refuse(string message) => new(MigrationAction.Refuse, message);
}

/// <summary>Thrown at startup when <see cref="MigrationPlacement.Decide"/> refuses; the hosts print it and exit.</summary>
public sealed class RemoteDatabaseRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// Whether a host may migrate the database it is pointed at (ADR 0023, amendment of 2026-11-15).
///
/// <para>
/// Every host migrates at startup. When the laptop's desk points at the desktop's database, a
/// laptop on a branch with a new migration would apply it there, and the desktop's desk would go
/// on running older code against a newer schema. Nothing would throw on the laptop; the desktop
/// would break later, somewhere else. So only the machine that owns the database migrates it, and
/// a host on another machine's database checks that its code and the schema agree before it
/// starts. It does not seed or clean up either: that is the owner's startup too.
/// </para>
///
/// <para>
/// Pure, so every branch is tested without a host or a database.
/// </para>
/// </summary>
public static class MigrationPlacement
{
    /// <summary>The override: this host migrates and seeds the database even though it is remote.</summary>
    public const string AllowRemoteMigrationKey = "Data:AllowRemoteMigration";

    /// <param name="isOwnDatabase">From <see cref="DatabasePlacement.IsOwnDatabase"/>.</param>
    /// <param name="applied">Migration ids in the database's history table.</param>
    /// <param name="known">Migration ids this code's assembly holds.</param>
    /// <param name="allowRemoteMigration">The override, <see cref="AllowRemoteMigrationKey"/>.</param>
    /// <param name="host">"host:port", for the message only.</param>
    public static MigrationDecision Decide(bool isOwnDatabase, IEnumerable<string> applied,
        IEnumerable<string> known, bool allowRemoteMigration, string host = "(remote)")
    {
        if (isOwnDatabase || allowRemoteMigration)
            return MigrationDecision.Migrate;

        var appliedSet = applied.ToHashSet(StringComparer.Ordinal);
        var knownSet = known.ToHashSet(StringComparer.Ordinal);

        var pending = knownSet.Where(id => !appliedSet.Contains(id)).Order(StringComparer.Ordinal).ToList();
        var unknown = appliedSet.Where(id => !knownSet.Contains(id)).Order(StringComparer.Ordinal).ToList();

        if (pending.Count == 0 && unknown.Count == 0)
            return MigrationDecision.Proceed;

        var opening = $"Refusing to start: the database at '{host}' is not this machine's own, and its schema "
            + "is not the one this code was built for. ";

        if (unknown.Count > 0 && pending.Count > 0)
            return MigrationDecision.Refuse(opening
                + $"It has {Count(unknown)} this code does not know ({string.Join(", ", unknown)}) and lacks "
                + $"{Count(pending)} this code has ({string.Join(", ", pending)}): the two machines are on "
                + "branches that have each added migrations. Put this machine on the commit the database's "
                + "owner runs, or switch it to its own database (scripts/connect-remote-db.ps1 -Local).");

        if (unknown.Count > 0)
            return MigrationDecision.Refuse(opening
                + $"It has {Count(unknown)} this code does not know ({string.Join(", ", unknown)}), so this "
                + "code is older than the database. Pull the newer commit the database's owner runs, "
                + "then start again.");

        return MigrationDecision.Refuse(opening
            + $"This code has {Count(pending)} the database has not applied ({string.Join(", ", pending)}), "
            + "so this code is newer than the database. Migrating it from here would leave the machine that "
            + "owns it running older code against a newer schema. Migrate it from the machine that owns the "
            + "database (start its desk on this commit), or, if this machine is deliberately to migrate it, set "
            + $"{AllowRemoteMigrationKey}=true.");
    }

    private static string Count(List<string> ids) => ids.Count == 1 ? "1 migration" : $"{ids.Count} migrations";
}
