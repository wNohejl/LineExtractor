using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using static LineOps.Data.CrossReference.GameLogSplits;

namespace LineOps.Tests.Analytics;

/// <summary>
/// The split rules, exercised without a database.
///
/// These are filters over rows the UI already holds, so they are pure — which is the point of
/// keeping them out of the service. The cases worth pinning are the ones where a plausible
/// implementation is subtly wrong: counting scheduled games in "last 5", and folding pushes
/// into an ATS record.
/// </summary>
public class GameLogSplitsTests
{
    private static TeamGameLogRow Row(
        int id,
        bool home = true,
        int? forScore = 5,
        int? againstScore = 3,
        GameStatus status = GameStatus.Final,
        string opponent = "Rivals",
        EntryResult? ats = null,
        EntryResult? total = null,
        decimal? spread = null,
        decimal? totalLine = null)
        => new(
            GameId: id,
            StartsAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-id),
            Opponent: opponent,
            Home: home,
            ScoreFor: forScore,
            ScoreAgainst: againstScore,
            Status: status,
            ClosingSpread: spread,
            AtsResult: ats,
            ClosingTotal: totalLine,
            TotalResult: total);

    [Fact]
    public void Home_split_keeps_only_home_games()
    {
        var log = new[] { Row(1), Row(2, home: false), Row(3) };

        var result = Apply(log, Split.Home);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Home));
    }

    [Fact]
    public void Away_split_keeps_only_away_games()
    {
        var log = new[] { Row(1), Row(2, home: false), Row(3, home: false) };

        var result = Apply(log, Split.Away);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.False(r.Home));
    }

    [Fact]
    public void All_split_returns_the_log_untouched()
    {
        var log = new[] { Row(1), Row(2, status: GameStatus.Scheduled) };

        Assert.Equal(2, Apply(log, Split.All).Count);
    }

    /// <summary>
    /// The case a naive Take(5) gets wrong. A window holding upcoming fixtures would otherwise
    /// produce a "last 5" containing games that have not been played.
    /// </summary>
    [Fact]
    public void Last5_counts_games_played_not_rows_listed()
    {
        var log = new[]
        {
            Row(1, status: GameStatus.Scheduled),
            Row(2, status: GameStatus.Scheduled),
            Row(3),
            Row(4),
            Row(5),
            Row(6),
            Row(7),
            Row(8)
        };

        var result = Apply(log, Split.Last5);

        Assert.Equal(5, result.Count);
        Assert.All(result, r => Assert.Equal(GameStatus.Final, r.Status));
        Assert.Equal([3, 4, 5, 6, 7], result.Select(r => r.GameId));
    }

    [Fact]
    public void Last10_returns_everything_when_fewer_games_have_been_played()
    {
        var log = new[] { Row(1), Row(2), Row(3, status: GameStatus.Live) };

        Assert.Equal(2, Apply(log, Split.Last10).Count);
    }

    [Fact]
    public void Versus_opponent_matches_regardless_of_casing()
    {
        var log = new[] { Row(1, opponent: "Giants"), Row(2, opponent: "Rockies") };

        var result = VersusOpponent(log, "giants");

        Assert.Single(result);
        Assert.Equal(1, result[0].GameId);
    }

    [Fact]
    public void Opponents_are_ordered_by_how_often_they_were_faced()
    {
        var log = new[]
        {
            Row(1, opponent: "Rockies"),
            Row(2, opponent: "Giants"),
            Row(3, opponent: "Giants"),
            Row(4, opponent: "Giants")
        };

        Assert.Equal(["Giants", "Rockies"], Opponents(log));
    }

    [Fact]
    public void Record_ignores_games_that_are_not_final()
    {
        var log = new[]
        {
            Row(1, forScore: 5, againstScore: 3),
            Row(2, forScore: 1, againstScore: 4),
            Row(3, status: GameStatus.Live, forScore: 2, againstScore: 0)
        };

        Assert.Equal((1, 1), Record(log));
    }

    /// <summary>
    /// An ATS record that folds pushes into either column misstates it: 8-2-3 is neither 8-2
    /// nor 8-5, and a bettor reading either would draw the wrong conclusion about the edge.
    /// </summary>
    [Fact]
    public void Ats_record_counts_pushes_separately()
    {
        var log = new[]
        {
            Row(1, ats: EntryResult.Win),
            Row(2, ats: EntryResult.Win),
            Row(3, ats: EntryResult.Loss),
            Row(4, ats: EntryResult.Push),
            Row(5, ats: null)
        };

        Assert.Equal((2, 1, 1), AtsRecord(log));
    }

    [Fact]
    public void Total_record_counts_overs_unders_and_pushes()
    {
        var log = new[]
        {
            Row(1, total: EntryResult.Win),
            Row(2, total: EntryResult.Loss),
            Row(3, total: EntryResult.Loss),
            Row(4, total: EntryResult.Push)
        };

        Assert.Equal((1, 2, 1), TotalRecord(log));
    }

    /// <summary>
    /// Coverage counts rows with any market data. A game with a total but no spread still had
    /// a line — reporting it as uncovered would understate what is actually held.
    /// </summary>
    [Fact]
    public void Coverage_counts_rows_carrying_any_closing_number()
    {
        var log = new[]
        {
            Row(1, spread: -1.5m),
            Row(2, totalLine: 8.5m),
            Row(3, spread: 2.5m, totalLine: 9m),
            Row(4)
        };

        Assert.Equal(3, WithClosingLine(log));
    }

    [Fact]
    public void A_row_with_no_line_reports_no_coverage()
    {
        Assert.False(Row(1).HasClosingLine);
    }

    /// <summary>
    /// Found by running it: an unplayed fixture carries zeroes rather than nulls, so a null
    /// check alone renders it as a nil-nil draw. A scheduled game showed "Result: 0-0" on the
    /// team log — the exact confusion ADR 0003 exists to prevent — so the status has to be part
    /// of the question.
    /// </summary>
    [Fact]
    public void A_scheduled_game_carrying_zeroes_has_no_score()
    {
        var scheduled = Row(1, forScore: 0, againstScore: 0, status: GameStatus.Scheduled);

        Assert.False(scheduled.HasScore);
        Assert.Null(scheduled.Won);
    }

    [Fact]
    public void A_genuine_nil_nil_that_was_played_does_have_a_score()
    {
        var played = Row(1, forScore: 0, againstScore: 0, status: GameStatus.Final);

        Assert.True(played.HasScore);
    }

    [Fact]
    public void A_live_game_has_a_score_but_still_no_result()
    {
        var live = Row(1, forScore: 2, againstScore: 1, status: GameStatus.Live);

        Assert.True(live.HasScore);
        Assert.Null(live.Won);
    }

    [Fact]
    public void A_postponed_game_has_no_score()
    {
        Assert.False(Row(1, forScore: 0, againstScore: 0, status: GameStatus.Postponed).HasScore);
    }
}
