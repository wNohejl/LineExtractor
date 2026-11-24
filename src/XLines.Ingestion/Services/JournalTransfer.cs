using System.Text.Json;
using XLines.Core.Entities;
using XLines.Core.Sync;
using XLines.Data;
using XLines.Data.Changes;
using Microsoft.EntityFrameworkCore;

namespace XLines.Ingestion.Services;

/// <summary>Counts for one kind of row in an import's plan.</summary>
public sealed class MergeTally
{
    public int New { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Unchanged { get; set; }
    public int Conflicts { get; set; }
    public int Unresolved { get; set; }

    public void Count(MergeAction action)
    {
        switch (action)
        {
            case MergeAction.Insert: New++; break;
            case MergeAction.Update: Updated++; break;
            case MergeAction.Delete: Deleted++; break;
            case MergeAction.Conflict: Conflicts++; break;
            default: Unchanged++; break;
        }
    }

    public override string ToString()
        => $"{New} new, {Updated} updated, {Deleted} deleted, {Unchanged} unchanged, {Conflicts} conflicts, {Unresolved} unresolved";
}

/// <summary>What an import did, or with <c>--dry-run</c> would do, and every row it could not decide or place.</summary>
public sealed class JournalImportPlan
{
    public MergeTally Entries { get; } = new();
    public MergeTally Parlays { get; } = new();
    public MergeTally Settings { get; } = new();

    /// <summary>Rows changed on both machines at the same moment, left as they are here.</summary>
    public List<string> Conflicts { get; } = [];

    /// <summary>Games and players the file names that this database does not hold, and what was done about each.</summary>
    public List<string> Unresolved { get; } = [];

    public bool Writes => new[] { Entries, Parlays, Settings }.Any(t => t.New + t.Updated + t.Deleted > 0);

    public IEnumerable<string> Lines()
    {
        yield return $"entries:  {Entries}";
        yield return $"parlays:  {Parlays}";
        yield return $"settings: {Settings}";

        foreach (var conflict in Conflicts)
            yield return $"conflict: {conflict}";

        foreach (var unresolved in Unresolved)
            yield return $"unresolved: {unresolved}";
    }
}

/// <summary>
/// The journal written to a file and merged back from one (ADR 0025): <c>--job journal:export</c>
/// and <c>--job journal:import</c>.
///
/// <para>
/// The export is every entry, parlay, deletion and travelling setting, with games and players
/// named by provider (<see cref="JournalFile"/>). The import resolves those names against this
/// database, decides each row with <see cref="JournalMerge"/>, and writes the result in one
/// transaction: a file that cannot be merged whole changes nothing. A game this database does
/// not hold is not guessed at; the entry comes in without one, and the report names the game,
/// so the operator can pull that day and import again, which then links it.
/// </para>
/// </summary>
public class JournalTransfer(XLinesDbContext db)
{
    public const string ExportJob = "journal:export";
    public const string ImportJob = "journal:import";

    public const string Usage =
        "The journal travels as a file: --job journal:export --out data/personal/journal.json, then on the other machine "
        + "--job journal:import --in data/personal/journal.json [--dry-run true]. The file must not be one git would commit.";

    /// <summary>A fallback match may move this far from the file's start: a rescheduled first pitch, not another day's game.</summary>
    public static readonly TimeSpan FallbackStartTolerance = TimeSpan.FromHours(12);

    // ---- Export --------------------------------------------------------------------------------

    public async Task<JournalFile> ReadAsync(CancellationToken ct = default)
    {
        var entries = await db.JournalEntries.AsNoTracking()
            .Include(e => e.Game).ThenInclude(g => g!.Sport)
            .Include(e => e.Game).ThenInclude(g => g!.HomeTeam)
            .Include(e => e.Game).ThenInclude(g => g!.AwayTeam)
            .Include(e => e.Player).ThenInclude(p => p!.Sport)
            .Include(e => e.Player).ThenInclude(p => p!.Team)
            .AsSplitQuery()
            .OrderBy(e => e.PlacedAt).ThenBy(e => e.SyncId)
            .ToListAsync(ct);

        var parlays = await db.Parlays.AsNoTracking().OrderBy(p => p.PlacedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var tombstones = await db.JournalTombstones.AsNoTracking().OrderBy(t => t.DeletedAtUtc).ThenBy(t => t.SyncId).ToListAsync(ct);
        var settings = await db.AppSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);

        return new JournalFile
        {
            ExportedAtUtc = JournalAuditInterceptor.Now(),
            Machine = Environment.MachineName,
            Entries = entries.Select(ToFile).ToList(),
            Parlays = parlays.Select(p => new JournalFileParlay
            {
                Id = p.Id,
                UpdatedAtUtc = p.UpdatedAtUtc,
                Book = p.Book,
                Stake = p.Stake,
                PriceQuoted = p.PriceQuoted,
                PlacedAt = p.PlacedAt,
                Result = p.Result,
                Payout = p.Payout,
                SettledAt = p.SettledAt,
                Note = p.Note
            }).ToList(),
            Tombstones = tombstones.Select(t => new JournalFileTombstone(t.SyncId, t.Kind, t.DeletedAtUtc)).ToList(),
            Settings = settings.Where(s => !JournalFile.MachineSettings.Contains(s.Key))
                .Select(s => new JournalFileSetting(s.Key, s.Value, s.UpdatedAtUtc)).ToList()
        };
    }

    /// <summary>Writes the journal to <paramref name="path"/>, refusing a path git would commit, and logs the transfer.</summary>
    public async Task<string> ExportAsync(string path, CancellationToken ct = default)
    {
        if (RepositoryGuard.Refusal(path) is { } refusal)
            throw new InvalidOperationException(refusal);

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var file = await ReadAsync(ct);

        // Written beside itself and moved, so a failed write never replaces the last good file.
        var partial = full + ".partial";
        await File.WriteAllTextAsync(partial, file.Serialize(), ct);
        File.Move(partial, full, overwrite: true);

        db.DataTransfers.Add(Transfer(DataTransferKind.JournalExported, new
        {
            file = Path.GetFileName(full),
            entries = file.Entries.Count,
            parlays = file.Parlays.Count,
            tombstones = file.Tombstones.Count,
            settings = file.Settings.Count
        }));
        await db.SaveChangesAsync(ct);

        return $"Journal written: {full}. {file.Entries.Count} entries, {file.Parlays.Count} parlays, "
               + $"{file.Tombstones.Count} deletions, {file.Settings.Count} settings.";
    }

    private static JournalFileEntry ToFile(JournalEntry e) => new()
    {
        SyncId = e.SyncId,
        UpdatedAtUtc = e.UpdatedAtUtc,
        Game = e.Game is { } g ? new GameReference(
            g.Sport!.Key, new Dictionary<string, string>(g.ExternalIds), g.StartsAt.ToUniversalTime(),
            Team(g.HomeTeam!), Team(g.AwayTeam!), g.DoubleHeaderGame) : null,
        Player = e.Player is { } p ? new PlayerReference(
            p.Sport!.Key, p.FullName, new Dictionary<string, string>(p.ExternalIds), p.Team?.Name) : null,
        ParlayId = e.ParlayGroupId,
        Market = e.Market,
        Outcome = e.Outcome,
        FreeTextMarket = e.FreeTextMarket,
        Book = e.Book,
        LineTaken = e.LineTaken,
        PriceTaken = e.PriceTaken,
        ManualPrice = e.ManualPrice,
        Stake = e.Stake,
        PlacedAt = e.PlacedAt,
        Result = e.Result,
        Payout = e.Payout,
        SettledAt = e.SettledAt,
        ClosingCapturedAt = e.ClosingCapturedAt,
        ClosingPrice = e.ClosingPrice,
        ClosingPoints = e.ClosingPoints,
        ClosingBook = e.ClosingBook,
        ClosingFairProbability = e.ClosingFairProbability,
        ClosingFairBasis = e.ClosingFairBasis,
        Note = e.Note
    };

    private static TeamReference Team(Team t) => new(t.Name, t.Abbrev, new Dictionary<string, string>(t.ExternalIds));

    // ---- Import --------------------------------------------------------------------------------

    /// <summary>Merges the file at <paramref name="path"/> into this journal, or with <paramref name="dryRun"/> only says what it would do.</summary>
    public async Task<JournalImportPlan> ImportAsync(string path, bool dryRun, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"No journal file at {full}. Export one on the other machine (--job {ExportJob}) and copy it here.", full);

        var file = JournalFile.Parse(await File.ReadAllTextAsync(full, ct));
        return await MergeAsync(file, Path.GetFileName(full), dryRun, ct);
    }

    /// <summary>Merges a file already read. All in one transaction, retried whole if the connection drops.</summary>
    public async Task<JournalImportPlan> MergeAsync(JournalFile file, string? fileName, bool dryRun, CancellationToken ct = default)
    {
        if (dryRun)
        {
            db.ChangeTracker.Clear();
            var plan = await PlanAsync(file, ct);
            db.ChangeTracker.Clear();
            return plan;
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var plan = await PlanAsync(file, ct);

            db.DataTransfers.Add(Transfer(DataTransferKind.JournalImported, new
            {
                file = fileName,
                exportedAtUtc = file.ExportedAtUtc,
                from = file.Machine,
                entries = Tally(plan.Entries),
                parlays = Tally(plan.Parlays),
                settings = Tally(plan.Settings),
                conflicts = plan.Conflicts.Count
            }));

            db.ImportingJournal = true;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                db.ImportingJournal = false;
            }

            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return plan;
        });
    }

    /// <summary>
    /// Reads this journal, decides every row of the file, and stages the writes on the context
    /// without saving them. The caller saves (an import) or clears them (a dry run).
    /// </summary>
    private async Task<JournalImportPlan> PlanAsync(JournalFile file, CancellationToken ct)
    {
        var plan = new JournalImportPlan();

        var entries = await db.JournalEntries.ToDictionaryAsync(e => e.SyncId, ct);
        var parlays = await db.Parlays.ToDictionaryAsync(p => p.Id, ct);
        var tombstones = await db.JournalTombstones.ToDictionaryAsync(t => t.SyncId, ct);
        var settings = await db.AppSettings.ToDictionaryAsync(s => s.Key, ct);
        var resolver = new ReferenceResolver(db);

        // Deletions first, so a parlay the file deleted is gone before its legs are decided, and
        // so their tombstones carry the file's moment of deletion rather than the import's.
        var deletedHere = new HashSet<Guid>();

        foreach (var t in file.Tombstones)
        {
            if (tombstones.ContainsKey(t.SyncId))
                continue;

            if (t.Kind == JournalTombstone.ParlayKind)
            {
                var local = parlays.GetValueOrDefault(t.SyncId);
                var action = JournalMerge.Tombstone<ParlayContent>(local is null ? null : new Versioned<ParlayContent>(local.UpdatedAtUtc, ParlayContent.Of(local)), t.DeletedAtUtc);
                plan.Parlays.Count(action);

                if (action == MergeAction.Delete)
                {
                    // Its legs go with it, as a parlay deleted on the desk takes them.
                    foreach (var leg in entries.Values.Where(e => e.ParlayGroupId == local!.Id).ToList())
                    {
                        db.JournalEntries.Remove(leg);
                        entries.Remove(leg.SyncId);
                        deletedHere.Add(leg.SyncId);
                        plan.Entries.Deleted++;
                    }

                    db.Parlays.Remove(local!);
                    parlays.Remove(local!.Id);
                }

                // A deletion this database had not heard of is kept either way, so it travels on.
                if (action == MergeAction.Delete || local is null)
                    AddTombstone(t);
            }
            else
            {
                var local = entries.GetValueOrDefault(t.SyncId);
                var action = deletedHere.Contains(t.SyncId)
                    ? MergeAction.Unchanged
                    : JournalMerge.Tombstone<EntryContent>(local is null ? null : new Versioned<EntryContent>(local.UpdatedAtUtc, EntryContent.Of(local)), t.DeletedAtUtc);

                if (!deletedHere.Contains(t.SyncId))
                    plan.Entries.Count(action);

                if (action == MergeAction.Delete)
                {
                    db.JournalEntries.Remove(local!);
                    entries.Remove(local!.SyncId);
                }

                if (action == MergeAction.Delete || local is null)
                    AddTombstone(t);
            }
        }

        void AddTombstone(JournalFileTombstone t)
        {
            var row = new JournalTombstone { SyncId = t.SyncId, Kind = t.Kind, DeletedAtUtc = t.DeletedAtUtc };
            db.JournalTombstones.Add(row);
            tombstones[t.SyncId] = row;
        }

        // Parlays, so a leg's parlay is here before the leg is.
        foreach (var p in file.Parlays)
        {
            var local = parlays.GetValueOrDefault(p.Id);
            var incoming = new Versioned<ParlayContent>(p.UpdatedAtUtc, ParlayContent.Of(p));
            var action = JournalMerge.Row(
                local is null ? null : new Versioned<ParlayContent>(local.UpdatedAtUtc, ParlayContent.Of(local)),
                tombstones.GetValueOrDefault(p.Id)?.DeletedAtUtc, incoming);
            plan.Parlays.Count(action);

            switch (action)
            {
                case MergeAction.Insert:
                    var parlay = new Parlay { Id = p.Id, UpdatedAtUtc = p.UpdatedAtUtc };
                    incoming.Content.ApplyTo(parlay);
                    db.Parlays.Add(parlay);
                    parlays[p.Id] = parlay;
                    Resurrect(p.Id);
                    break;

                case MergeAction.Update:
                    incoming.Content.ApplyTo(local!);
                    local!.UpdatedAtUtc = p.UpdatedAtUtc;
                    break;

                case MergeAction.Conflict:
                    plan.Conflicts.Add($"{p.Describe()} ({p.Id}) was changed on both machines at {p.UpdatedAtUtc:O}; kept as it is here.");
                    break;
            }
        }

        foreach (var e in file.Entries)
        {
            var local = entries.GetValueOrDefault(e.SyncId);

            var game = await resolver.GameAsync(e.Game, ct);
            var player = await resolver.PlayerAsync(e.Player, ct);
            var gameUnresolved = e.Game is not null && game is null;
            var playerUnresolved = e.Player is not null && player is null;

            var content = JournalMerge.KeepKnownLinks(
                EntryContent.Of(e, game, player), local is null ? null : EntryContent.Of(local), gameUnresolved, playerUnresolved);
            var incoming = new Versioned<EntryContent>(e.UpdatedAtUtc, content);

            var action = JournalMerge.Entry(
                local is null ? null : new Versioned<EntryContent>(local.UpdatedAtUtc, EntryContent.Of(local)),
                tombstones.GetValueOrDefault(e.SyncId)?.DeletedAtUtc, incoming);

            // A leg whose parlay is not here has nowhere to go: alone it would read as a straight
            // bet at no stake.
            if (action is MergeAction.Insert or MergeAction.Update && e.ParlayId is { } parlayId && !parlays.ContainsKey(parlayId))
            {
                plan.Entries.Unresolved++;
                plan.Unresolved.Add($"{e.Describe()}: a leg of parlay {parlayId}, which this database does not hold; skipped.");
                continue;
            }

            plan.Entries.Count(action);

            // Said whenever the entry, as it stands after this import, lacks a game or player the
            // file named: brought in without it, or already here without it. Not for an entry that
            // stays deleted here.
            var lacks = (gameUnresolved && content.GameId is null) || (playerUnresolved && content.PlayerId is null);
            if (lacks && (local is not null || action is MergeAction.Insert))
            {
                plan.Entries.Unresolved++;
                var what = string.Join(" and ", new[]
                {
                    gameUnresolved ? $"game {e.Game}" : null,
                    playerUnresolved ? $"player {e.Player}" : null
                }.OfType<string>());
                plan.Unresolved.Add($"{e.Describe()}: {what} is not in this database; "
                                    + $"{(action is MergeAction.Insert or MergeAction.Update ? "kept without it" : "left as it is")}. "
                                    + "Pull that day's games, then import again to link it.");
            }

            switch (action)
            {
                case MergeAction.Insert:
                    var entry = new JournalEntry { SyncId = e.SyncId, UpdatedAtUtc = e.UpdatedAtUtc };
                    content.ApplyTo(entry);
                    entry.ClosingSnapshotId = await ClosingRowAsync(entry, ct);
                    db.JournalEntries.Add(entry);
                    entries[e.SyncId] = entry;
                    Resurrect(e.SyncId);
                    break;

                case MergeAction.Update:
                    var closeMoved = local!.ClosingCapturedAt != content.ClosingCapturedAt
                                     || local.ClosingBook != content.ClosingBook
                                     || local.ClosingPrice != content.ClosingPrice
                                     || local.GameId != content.GameId;
                    content.ApplyTo(local);
                    if (closeMoved)
                        local.ClosingSnapshotId = await ClosingRowAsync(local, ct);
                    local.UpdatedAtUtc = e.UpdatedAtUtc > local.UpdatedAtUtc ? e.UpdatedAtUtc : local.UpdatedAtUtc;
                    break;

                case MergeAction.Conflict:
                    plan.Conflicts.Add($"{e.Describe()} ({e.SyncId}) was changed on both machines at {e.UpdatedAtUtc:O}; kept as it is here.");
                    break;
            }
        }

        foreach (var s in file.Settings)
        {
            if (JournalFile.MachineSettings.Contains(s.Key))
                continue;

            var local = settings.GetValueOrDefault(s.Key);
            var action = JournalMerge.Row(
                local is null ? null : new Versioned<string>(local.UpdatedAtUtc, local.Value),
                null, new Versioned<string>(s.UpdatedAtUtc, s.Value));
            plan.Settings.Count(action);

            switch (action)
            {
                case MergeAction.Insert:
                    db.AppSettings.Add(new AppSetting { Key = s.Key, Value = s.Value, UpdatedAtUtc = s.UpdatedAtUtc });
                    break;
                case MergeAction.Update:
                    local!.Value = s.Value;
                    local.UpdatedAtUtc = s.UpdatedAtUtc;
                    break;
                case MergeAction.Conflict:
                    plan.Conflicts.Add($"setting {s.Key} is \"{local!.Value}\" here and \"{s.Value}\" in the file, both from {s.UpdatedAtUtc:O}; kept as it is here.");
                    break;
            }
        }

        return plan;

        // A row the file brings back after this database deleted it: the deletion is undone, so
        // the next export does not carry both.
        void Resurrect(Guid id)
        {
            if (tombstones.Remove(id, out var stone))
                db.JournalTombstones.Remove(stone);
        }
    }

    /// <summary>
    /// The close an imported entry was priced against, as this database numbers it. The file
    /// carries the close's values, which are what CLV reads; the row id is only this database's
    /// mark that the search is done (SettlementService), so it is looked up here, and left null
    /// where this database has no such close.
    /// </summary>
    private async Task<long?> ClosingRowAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.GameId is not { } gameId || entry.ClosingCapturedAt is not { } at || entry.ClosingBook is not { } book)
            return null;

        var id = await db.ClosingLines.AsNoTracking()
            .Where(c => c.GameId == gameId && c.Book == book && c.Market == entry.Market
                        && c.Outcome == entry.Outcome && c.CapturedAt == at)
            .Select(c => (long?)c.Id)
            .FirstOrDefaultAsync(ct);

        return id;
    }

    private static object Tally(MergeTally t)
        => new { @new = t.New, updated = t.Updated, deleted = t.Deleted, unchanged = t.Unchanged, conflicts = t.Conflicts, unresolved = t.Unresolved };

    private static DataTransfer Transfer(DataTransferKind kind, object detail) => new()
    {
        Kind = kind,
        AtUtc = JournalAuditInterceptor.Now(),
        Machine = Environment.MachineName,
        Detail = JsonSerializer.Serialize(detail, JournalFile.Json)
    };
}
