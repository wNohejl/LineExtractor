using LineOps.Core.Entities;
using LineOps.Data.CrossReference;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The recent record the Game window's Team data tab prints under each club, exercised without a
/// database.
///
/// A loss is a final the side scored fewer in. <see cref="TeamRecentGame.Won"/> grades a final
/// as for &gt; against, so a level final arrives as "not won", and counting every "not won" as
/// a loss turned a tie into one — the same mistake <see cref="GameLogSplits.Record"/> stopped
/// making for the Team window. Ties are counted beside the two, and written only when there is
/// one, so a baseball record keeps the two numbers it has always had.
/// </summary>
public class TeamFormRecordTests
{
    private static TeamRecentGame Game(int id, int? scoreFor, int? scoreAgainst, GameStatus status = GameStatus.Final)
        => new(id, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-id), "Rivals", true,
            scoreFor, scoreAgainst, status);

    private static TeamForm Form(params TeamRecentGame[] recent)
        => new(new Team { Name = "Kansas City Chiefs", Abbrev = "KC" }, recent, []);

    [Fact]
    public void A_tied_final_is_a_tie_not_a_loss()
    {
        var form = Form(Game(1, 24, 17), Game(2, 20, 20), Game(3, 10, 13));

        Assert.Equal(1, form.Wins);
        Assert.Equal(1, form.Losses);
        Assert.Equal(1, form.Ties);
        Assert.Equal("1-1-1", form.Record);
    }

    [Fact]
    public void A_record_without_a_tie_keeps_two_numbers()
    {
        var form = Form(Game(1, 5, 3), Game(2, 2, 4), Game(3, 7, 1));

        Assert.Equal(0, form.Ties);
        Assert.Equal("2-1", form.Record);
    }

    [Fact]
    public void An_unfinished_game_is_neither_a_loss_nor_a_tie()
    {
        // A live game level at 0-0, and an unplayed fixture ESPN wrote zeroes for.
        var form = Form(Game(1, 0, 0, GameStatus.Live), Game(2, 0, 0, GameStatus.Scheduled), Game(3, 3, 2));

        Assert.Equal("1-0", form.Record);
    }
}
