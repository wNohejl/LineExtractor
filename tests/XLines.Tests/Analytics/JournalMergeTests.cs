using XLines.Core.Entities;
using XLines.Core.Sync;

namespace XLines.Tests.Analytics;

/// <summary>
/// The journal merge's decisions (ADR 0025), as pure functions: the newer copy wins, a tie that
/// differs is a conflict, a deletion beats only an older copy, and a link a copy lacked is taken.
/// </summary>
public class JournalMergeTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static EntryContent Content(string? note = null, int? gameId = 7, int? playerId = null) => new(
        gameId, playerId, null, Markets.Moneyline, "Buffalo Bills", null, "draftkings", null, -120m, false, 100m,
        new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.Zero), EntryResult.Pending, null, null,
        null, null, null, null, null, null, note);

    private static Versioned<EntryContent> V(DateTime at, EntryContent c) => new(at, c);

    [Fact]
    public void A_row_not_here_is_inserted()
        => Assert.Equal(MergeAction.Insert, JournalMerge.Entry(null, null, V(T0, Content())));

    [Fact]
    public void The_same_row_is_unchanged_whatever_its_timestamps_say()
    {
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(V(T0, Content("a")), null, V(T0.AddHours(1), Content("a"))));
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(V(T0, Content("a")), null, V(T0, Content("a"))));
    }

    [Fact]
    public void The_newer_copy_wins_and_the_older_is_left_alone()
    {
        Assert.Equal(MergeAction.Update, JournalMerge.Entry(V(T0, Content("here")), null, V(T0.AddTicks(10), Content("file"))));
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(V(T0, Content("here")), null, V(T0.AddTicks(-10), Content("file"))));
    }

    [Fact]
    public void Two_different_copies_from_the_same_moment_are_a_conflict()
        => Assert.Equal(MergeAction.Conflict, JournalMerge.Entry(V(T0, Content("here")), null, V(T0, Content("file"))));

    [Fact]
    public void A_row_deleted_here_after_the_file_last_changed_it_stays_deleted()
    {
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(null, T0.AddMinutes(1), V(T0, Content())));
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(null, T0, V(T0, Content())));
    }

    [Fact]
    public void A_row_changed_elsewhere_after_it_was_deleted_here_comes_back()
        => Assert.Equal(MergeAction.Insert, JournalMerge.Entry(null, T0, V(T0.AddMinutes(1), Content())));

    [Fact]
    public void A_deletion_newer_than_the_copy_here_deletes_it()
        => Assert.Equal(MergeAction.Delete, JournalMerge.Tombstone<EntryContent>(V(T0, Content()), T0.AddSeconds(1)));

    [Fact]
    public void A_copy_changed_here_after_the_deletion_survives_it()
    {
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Tombstone<EntryContent>(V(T0.AddSeconds(1), Content()), T0));
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Tombstone<EntryContent>(V(T0, Content()), T0));
    }

    [Fact]
    public void A_deletion_of_a_row_never_here_changes_nothing()
        => Assert.Equal(MergeAction.Unchanged, JournalMerge.Tombstone<EntryContent>(null, T0));

    [Fact]
    public void A_copy_that_only_adds_the_game_this_one_lacked_is_taken_even_if_older()
    {
        var here = V(T0, Content(gameId: null));
        var file = V(T0.AddDays(-1), Content(gameId: 7));

        Assert.True(JournalMerge.Links(here.Content, file.Content));
        Assert.Equal(MergeAction.Update, JournalMerge.Entry(here, null, file));
    }

    [Fact]
    public void A_copy_that_drops_a_game_is_not_a_link()
    {
        Assert.False(JournalMerge.Links(Content(gameId: 7), Content(gameId: null)));
        Assert.False(JournalMerge.Links(Content(gameId: 7), Content(gameId: 8)));
    }

    [Fact]
    public void A_game_the_file_cannot_name_here_keeps_the_one_this_copy_knows()
    {
        var file = Content(gameId: null, note: "x");
        var here = Content(gameId: 7, note: "x");

        var compared = JournalMerge.KeepKnownLinks(file, here, gameUnresolved: true, playerUnresolved: false);

        Assert.Equal(7, compared.GameId);
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Entry(V(T0, here), null, V(T0.AddHours(1), compared)));
    }

    [Fact]
    public void Settings_merge_by_the_same_rule()
    {
        Assert.Equal(MergeAction.Update, JournalMerge.Row<string>(new(T0, "100"), null, new(T0.AddHours(1), "250")));
        Assert.Equal(MergeAction.Conflict, JournalMerge.Row<string>(new(T0, "100"), null, new(T0, "250")));
        Assert.Equal(MergeAction.Unchanged, JournalMerge.Row<string>(new(T0, "100"), null, new(T0.AddHours(1), "100")));
    }

    [Fact]
    public void A_file_of_another_format_is_refused()
    {
        var ex = Assert.Throws<FormatException>(() => JournalFile.Parse("""{ "format": 2, "entries": [] }"""));
        Assert.Contains("format 2", ex.Message);
        Assert.Throws<FormatException>(() => JournalFile.Parse("""{ "entries": [] }"""));
    }

    [Fact]
    public void A_file_reads_back_as_it_was_written()
    {
        var file = new JournalFile
        {
            ExportedAtUtc = T0,
            Machine = "DESKTOP",
            Entries =
            [
                new JournalFileEntry
                {
                    SyncId = Guid.NewGuid(), UpdatedAtUtc = T0, Market = Markets.Spread, Outcome = "Bills", Book = "fanduel",
                    LineTaken = -2.5m, PriceTaken = 104.348m, Stake = 50m, PlacedAt = T0, Result = EntryResult.Win, Payout = 102.17m,
                    ClosingFairProbability = 0.5123456789,
                    Game = new GameReference("nfl", new() { ["espn"] = "401", ["the-odds-api"] = "abc" }, T0,
                        new TeamReference("Buffalo Bills", "BUF", new() { ["espn"] = "2" }),
                        new TeamReference("Miami Dolphins", "MIA", new()))
                }
            ],
            Tombstones = [new JournalFileTombstone(Guid.NewGuid(), JournalTombstone.EntryKind, T0)],
            Settings = [new JournalFileSetting(AppSetting.StartingBankroll, "1000", T0)]
        };

        var back = JournalFile.Parse(file.Serialize());
        var entry = Assert.Single(back.Entries);

        Assert.Equal(1, back.Format);
        Assert.Contains("\"format\": 1", file.Serialize());
        Assert.Contains("\"the-odds-api\": \"abc\"", file.Serialize());
        Assert.Equal(file.Entries[0].SyncId, entry.SyncId);
        Assert.Equal(DateTimeKind.Utc, entry.UpdatedAtUtc.Kind);
        Assert.Equal(EntryContent.Of(file.Entries[0], 1, null), EntryContent.Of(entry, 1, null));
        Assert.Equal("401", entry.Game!.ExternalIds["espn"]);
    }
}
