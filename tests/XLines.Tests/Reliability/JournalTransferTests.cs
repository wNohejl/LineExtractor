using XLines.Core.Entities;
using XLines.Core.Sync;
using XLines.Data;
using XLines.Ingestion.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace XLines.Tests.Reliability;

/// <summary>
/// The journal file between two machines (ADR 0025), against two real Postgres databases that
/// number their games differently: a game is found by its provider id, not its row id; a game
/// the other database lacks is named, not guessed; an edit on both sides is a conflict; a
/// deletion beats an older edit and loses to a newer one; and importing the same file twice
/// changes nothing the second time. Also the audit and tombstones every journal write leaves.
/// </summary>
[Collection(PostgresCollection.Name)]
public class JournalTransferTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2025, 9, 7, 17, 0, 0, TimeSpan.Zero);

    /// <summary>A fresh, migrated database in the fixture's server: the laptop beside the desktop.</summary>
    private async Task<string> NewDatabaseAsync()
    {
        var name = "jt_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = fixture.CreateContext())
        {
            // The name is made here, not taken from anywhere: nothing to inject.
            var create = "CREATE DATABASE \"" + name + "\"";
            await admin.Database.ExecuteSqlRawAsync(create);
        }

        var cs = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        await using var db = Open(cs);
        await db.Database.MigrateAsync();
        return cs;
    }

    private static XLinesDbContext Open(string cs)
        => new(new DbContextOptionsBuilder<XLinesDbContext>().UseNpgsql(cs).Options);

    private static JournalService Journal(XLinesDbContext db)
        => new(db, new SettlementService(db, NullLogger<SettlementService>.Instance));

    /// <summary>
    /// The NFL, its two sides and the one game, with <paramref name="fillers"/> other games added
    /// first so this database numbers the game differently from the other.
    /// </summary>
    private static async Task<int> SeedGameAsync(string cs, int fillers, string? espnId = "401772918", DateTimeOffset? startsAt = null)
    {
        await using var db = Open(cs);

        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == "nfl");
        if (sport is null)
        {
            sport = new Sport { Key = "nfl", Name = "NFL" };
            db.Sports.Add(sport);
            await db.SaveChangesAsync();
        }

        var bills = await db.Teams.FirstOrDefaultAsync(t => t.Name == "Buffalo Bills")
                    ?? db.Teams.Add(new Team { SportId = sport.Id, Name = "Buffalo Bills", Abbrev = "BUF", ExternalIds = new() { ["espn"] = "2" } }).Entity;
        var ravens = await db.Teams.FirstOrDefaultAsync(t => t.Name == "Baltimore Ravens")
                     ?? db.Teams.Add(new Team { SportId = sport.Id, Name = "Baltimore Ravens", Abbrev = "BAL", ExternalIds = new() { ["espn"] = "33" } }).Entity;
        await db.SaveChangesAsync();

        for (var i = 0; i < fillers; i++)
            db.Games.Add(new Game
            {
                SportId = sport.Id,
                HomeTeamId = ravens.Id,
                AwayTeamId = bills.Id,
                SeasonYear = 2024,
                StartsAt = Kickoff.AddDays(-400 - i),
                ExternalIds = new() { ["espn"] = $"filler-{i}" }
            });
        await db.SaveChangesAsync();

        var game = new Game
        {
            SportId = sport.Id,
            HomeTeamId = bills.Id,
            AwayTeamId = ravens.Id,
            SeasonYear = 2025,
            StartsAt = startsAt ?? Kickoff,
            Status = GameStatus.Final,
            HomeScore = 41,
            AwayScore = 40,
            ExternalIds = espnId is null ? new() : new() { ["espn"] = espnId }
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private static EntryDraft Bet(int? gameId, string? note = null)
        => new(gameId, Markets.Moneyline, null, "Buffalo Bills", null, -150m, 75m, "draftkings", note);

    private static async Task<JournalFile> ExportAsync(string cs)
    {
        await using var db = Open(cs);
        return await new JournalTransfer(db).ReadAsync();
    }

    private static async Task<JournalImportPlan> ImportAsync(string cs, JournalFile file, bool dryRun = false)
    {
        await using var db = Open(cs);
        return await new JournalTransfer(db).MergeAsync(file, "journal.json", dryRun);
    }

    [Fact]
    public async Task A_game_is_found_by_its_provider_id_where_the_other_database_numbers_it_differently()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        var onLaptop = await SeedGameAsync(laptop, fillers: 5);
        Assert.NotEqual(onDesktop, onLaptop);

        Guid syncId;
        await using (var db = Open(desktop))
            syncId = (await Journal(db).SaveEntryAsync(Bet(onDesktop, "on the Bills"))).SyncId;

        var plan = await ImportAsync(laptop, await ExportAsync(desktop));

        Assert.Equal(1, plan.Entries.New);
        Assert.Empty(plan.Unresolved);

        await using var check = Open(laptop);
        var entry = await check.JournalEntries.SingleAsync();
        Assert.Equal(syncId, entry.SyncId);
        Assert.Equal(onLaptop, entry.GameId);
        Assert.Equal("on the Bills", entry.Note);
    }

    [Fact]
    public async Task A_game_with_no_shared_provider_id_is_found_by_its_sides_and_start()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0, espnId: "401772918");
        var onLaptop = await SeedGameAsync(laptop, fillers: 3, espnId: null, startsAt: Kickoff.AddMinutes(20));

        await using (var db = Open(desktop))
            await Journal(db).SaveEntryAsync(Bet(onDesktop));

        await ImportAsync(laptop, await ExportAsync(desktop));

        await using var check = Open(laptop);
        Assert.Equal(onLaptop, (await check.JournalEntries.SingleAsync()).GameId);
    }

    [Fact]
    public async Task A_game_the_other_database_lacks_is_named_not_guessed_and_linked_once_it_is_pulled()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);

        // The laptop holds a game between the same sides a week earlier, under another ESPN id: not this one.
        await SeedGameAsync(laptop, fillers: 1, espnId: "401000000", startsAt: Kickoff.AddDays(-7));

        await using (var db = Open(desktop))
            await Journal(db).SaveEntryAsync(Bet(onDesktop));

        var file = await ExportAsync(desktop);
        var plan = await ImportAsync(laptop, file);

        Assert.Equal(1, plan.Entries.New);
        Assert.Equal(1, plan.Entries.Unresolved);
        var said = Assert.Single(plan.Unresolved);
        Assert.Contains("Baltimore Ravens at Buffalo Bills", said);
        Assert.Contains("espn 401772918", said);

        await using (var check = Open(laptop))
            Assert.Null((await check.JournalEntries.SingleAsync()).GameId);

        // The day is pulled; the same file now links the entry, and says nothing is missing.
        var pulled = await SeedGameAsync(laptop, fillers: 0);
        var again = await ImportAsync(laptop, file);

        Assert.Equal(1, again.Entries.Updated);
        Assert.Empty(again.Unresolved);
        await using (var check = Open(laptop))
            Assert.Equal(pulled, (await check.JournalEntries.SingleAsync()).GameId);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_changes_nothing_the_second_time()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        await SeedGameAsync(laptop, fillers: 2);

        await using (var db = Open(desktop))
        {
            var journal = Journal(db);
            await journal.SaveEntryAsync(Bet(onDesktop, "straight"));
            await journal.SaveParlayAsync(new ParlayDraft("fanduel", 20m, 450m, "a parlay", [Bet(onDesktop), Bet(null) with { FreeTextMarket = "Allen over 1.5 TDs" }]));
            await journal.SetStartingBankrollAsync(1000m);
        }

        var file = await ExportAsync(desktop);
        var first = await ImportAsync(laptop, file);
        Assert.Equal(3, first.Entries.New);
        Assert.Equal(1, first.Parlays.New);
        Assert.Equal(1, first.Settings.New);

        long audits;
        await using (var db = Open(laptop))
            audits = await db.JournalAudit.LongCountAsync();

        var second = await ImportAsync(laptop, file);

        Assert.False(second.Writes);
        Assert.Equal(3, second.Entries.Unchanged);
        Assert.Equal(1, second.Parlays.Unchanged);
        Assert.Equal(1, second.Settings.Unchanged);
        Assert.Empty(second.Conflicts);

        await using (var db = Open(laptop))
        {
            Assert.Equal(audits, await db.JournalAudit.LongCountAsync());
            Assert.Equal(2, await db.DataTransfers.CountAsync(t => t.Kind == DataTransferKind.JournalImported));
            Assert.All(await db.JournalAudit.ToListAsync(), a => Assert.Equal(JournalAuditAction.Imported, a.Action));
        }

        // And the laptop's own export, read back on the desktop, is the desktop's journal again.
        var back = await ImportAsync(desktop, await ExportAsync(laptop));
        Assert.False(back.Writes);
    }

    [Fact]
    public async Task A_dry_run_says_what_it_would_do_and_writes_nothing()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        await SeedGameAsync(laptop, fillers: 0);

        await using (var db = Open(desktop))
            await Journal(db).SaveEntryAsync(Bet(onDesktop));

        var plan = await ImportAsync(laptop, await ExportAsync(desktop), dryRun: true);

        Assert.Equal(1, plan.Entries.New);
        await using var check = Open(laptop);
        Assert.Equal(0, await check.JournalEntries.CountAsync());
        Assert.Equal(0, await check.DataTransfers.CountAsync());
        Assert.Equal(0, await check.JournalAudit.CountAsync());
    }

    [Fact]
    public async Task An_edit_on_both_machines_is_won_by_the_newer_and_a_tie_is_left_as_a_conflict()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        var onLaptop = await SeedGameAsync(laptop, fillers: 4);

        int desktopId;
        await using (var db = Open(desktop))
            desktopId = (await Journal(db).SaveEntryAsync(Bet(onDesktop))).Id;
        await ImportAsync(laptop, await ExportAsync(desktop));

        int laptopId;
        await using (var db = Open(laptop))
            laptopId = (await db.JournalEntries.SingleAsync()).Id;

        // The laptop edits first, the desktop after: the desktop's is newer and wins.
        await using (var db = Open(laptop))
            await Journal(db).SaveEntryAsync(Bet(onLaptop, "laptop's note"), laptopId);
        await using (var db = Open(desktop))
            await Journal(db).SaveEntryAsync(Bet(onDesktop, "desktop's note"), desktopId);

        var newer = await ImportAsync(laptop, await ExportAsync(desktop));
        Assert.Equal(1, newer.Entries.Updated);
        await using (var db = Open(laptop))
            Assert.Equal("desktop's note", (await db.JournalEntries.SingleAsync()).Note);

        // Both edit again, and the two edits carry the same moment: neither can be ordered.
        await using (var db = Open(laptop))
            await Journal(db).SaveEntryAsync(Bet(onLaptop, "laptop again"), laptopId);
        await using (var db = Open(desktop))
            await Journal(db).SaveEntryAsync(Bet(onDesktop, "desktop again"), desktopId);

        var tie = new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc);
        foreach (var cs in new[] { desktop, laptop })
        {
            await using var db = Open(cs);
            await db.JournalEntries.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAtUtc, tie));
        }

        var conflicted = await ImportAsync(laptop, await ExportAsync(desktop));

        Assert.Equal(1, conflicted.Entries.Conflicts);
        Assert.Contains("changed on both machines", Assert.Single(conflicted.Conflicts));
        await using (var db = Open(laptop))
            Assert.Equal("laptop again", (await db.JournalEntries.SingleAsync()).Note);
    }

    [Fact]
    public async Task A_deletion_beats_an_older_edit_and_loses_to_a_newer_one()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        var onLaptop = await SeedGameAsync(laptop, fillers: 2);

        int kept, gone;
        await using (var db = Open(desktop))
        {
            kept = (await Journal(db).SaveEntryAsync(Bet(onDesktop, "kept"))).Id;
            gone = (await Journal(db).SaveEntryAsync(Bet(onDesktop, "gone"))).Id;
        }
        await ImportAsync(laptop, await ExportAsync(desktop));

        Dictionary<string, int> onLaptopIds;
        await using (var db = Open(laptop))
            onLaptopIds = await db.JournalEntries.ToDictionaryAsync(e => e.Note!, e => e.Id);

        // "gone": edited on the laptop, then deleted on the desktop — the deletion is newer.
        await using (var db = Open(laptop))
            await Journal(db).SaveEntryAsync(Bet(onLaptop, "gone, edited"), onLaptopIds["gone"]);
        await using (var db = Open(desktop))
            await Journal(db).DeleteEntryAsync(gone);

        // "kept": deleted on the desktop, then edited on the laptop — the edit is newer.
        await using (var db = Open(desktop))
            await Journal(db).DeleteEntryAsync(kept);
        await using (var db = Open(laptop))
            await Journal(db).SaveEntryAsync(Bet(onLaptop, "kept, edited after"), onLaptopIds["kept"]);

        var plan = await ImportAsync(laptop, await ExportAsync(desktop));

        Assert.Equal(1, plan.Entries.Deleted);
        await using (var db = Open(laptop))
        {
            var left = await db.JournalEntries.SingleAsync();
            Assert.Equal("kept, edited after", left.Note);
            Assert.Equal(1, await db.JournalTombstones.CountAsync());
        }

        // Back the other way: the desktop gets the surviving edit back, and keeps the deletion.
        var back = await ImportAsync(desktop, await ExportAsync(laptop));
        Assert.Equal(1, back.Entries.New);
        await using (var db = Open(desktop))
        {
            Assert.Equal("kept, edited after", (await db.JournalEntries.SingleAsync()).Note);
            Assert.Equal(1, await db.JournalTombstones.CountAsync());
        }
    }

    [Fact]
    public async Task A_parlay_deleted_on_one_machine_is_deleted_with_its_legs_on_the_other()
    {
        var desktop = await NewDatabaseAsync();
        var laptop = await NewDatabaseAsync();
        var onDesktop = await SeedGameAsync(desktop, fillers: 0);
        await SeedGameAsync(laptop, fillers: 1);

        Guid parlayId;
        await using (var db = Open(desktop))
            parlayId = (await Journal(db).SaveParlayAsync(new ParlayDraft("fanduel", 10m, null, null,
                [Bet(onDesktop), Bet(null) with { FreeTextMarket = "Henry 100+ yards" }]))).Id;
        await ImportAsync(laptop, await ExportAsync(desktop));

        await using (var db = Open(desktop))
            await Journal(db).DeleteParlayAsync(parlayId);

        var plan = await ImportAsync(laptop, await ExportAsync(desktop));

        Assert.Equal(1, plan.Parlays.Deleted);
        Assert.Equal(2, plan.Entries.Deleted);
        await using var check = Open(laptop);
        Assert.Equal(0, await check.Parlays.CountAsync());
        Assert.Equal(0, await check.JournalEntries.CountAsync());
        Assert.Equal(3, await check.JournalTombstones.CountAsync());
    }

    [Fact]
    public async Task Every_journal_write_is_stamped_audited_and_a_deletion_leaves_a_tombstone()
    {
        var cs = await NewDatabaseAsync();
        var gameId = await SeedGameAsync(cs, fillers: 0);

        await using var db = Open(cs);
        var journal = Journal(db);

        var entry = await journal.SaveEntryAsync(Bet(gameId));
        var created = entry.UpdatedAtUtc;
        Assert.NotEqual(default, created);
        Assert.NotEqual(Guid.Empty, entry.SyncId);

        // Saved again with nothing changed: no new stamp, no audit row.
        await journal.SaveEntryAsync(Bet(gameId), entry.Id);
        Assert.Equal(created, (await db.JournalEntries.AsNoTracking().SingleAsync()).UpdatedAtUtc);

        await journal.SaveEntryAsync(Bet(gameId, "a note"), entry.Id);
        // Settlement grades it (the game is final): a write too, through the same interceptor.
        await new SettlementService(db, NullLogger<SettlementService>.Instance).SettleAsync();
        await journal.DeleteEntryAsync(entry.Id);

        var audit = await db.JournalAudit.AsNoTracking().Where(a => a.EntrySyncId == entry.SyncId).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [JournalAuditAction.Created, JournalAuditAction.Updated, JournalAuditAction.Updated, JournalAuditAction.Deleted],
            audit.Select(a => a.Action));
        Assert.Null(audit[0].Before);
        Assert.Contains("\"Note\": null", audit[1].Before!.Replace("\":null", "\": null"));
        Assert.Contains("a note", audit[1].After!);
        Assert.Contains("Win", audit[2].After!);
        Assert.Null(audit[3].After);
        Assert.All(audit, a => Assert.Equal(Environment.MachineName, a.Machine));

        var stone = await db.JournalTombstones.AsNoTracking().SingleAsync();
        Assert.Equal(entry.SyncId, stone.SyncId);
        Assert.Equal(JournalTombstone.EntryKind, stone.Kind);
    }

    [Fact]
    public void A_journal_file_git_would_commit_is_refused()
    {
        var root = RepoRoot();

        Assert.NotNull(RepositoryGuard.Refusal(Path.Combine(root, "docs", "journal.json")));
        Assert.Null(RepositoryGuard.Refusal(Path.Combine(root, "data", "personal", "journal.json")));
        Assert.Null(RepositoryGuard.Refusal(Path.Combine(Path.GetTempPath(), "xlines-" + Guid.NewGuid().ToString("N"), "journal.json")));
    }

    [Fact]
    public void A_private_repository_in_an_ignored_folder_may_hold_the_journal()
    {
        // The operator's arrangement: the public repository ignores a folder, and that folder is
        // a repository of its own which tracks the journal on purpose.
        var outer = Path.Combine(Path.GetTempPath(), "xlines-" + Guid.NewGuid().ToString("N"));
        var inner = Path.Combine(outer, "private");
        Directory.CreateDirectory(inner);

        try
        {
            Git(outer, "init", "-q");
            File.WriteAllText(Path.Combine(outer, ".gitignore"), "private/\n");
            Git(inner, "init", "-q");

            Assert.Null(RepositoryGuard.Refusal(Path.Combine(inner, "journal.json")));
            Assert.NotNull(RepositoryGuard.Refusal(Path.Combine(outer, "journal.json")));

            // A nested repository the outer one does not ignore is no shelter: asked on its own
            // terms, it would commit the file.
            var open = Path.Combine(outer, "open");
            Directory.CreateDirectory(open);
            Git(open, "init", "-q");
            Assert.NotNull(RepositoryGuard.Refusal(Path.Combine(open, "journal.json")));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(outer, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(outer, recursive: true);
        }
    }

    private static void Git(string directory, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var git = System.Diagnostics.Process.Start(start)!;
        git.WaitForExit(15_000);
        Assert.Equal(0, git.ExitCode);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "XLines.slnx")))
                return dir.FullName;

        throw new InvalidOperationException("XLines.slnx not found above the test assembly.");
    }
}
