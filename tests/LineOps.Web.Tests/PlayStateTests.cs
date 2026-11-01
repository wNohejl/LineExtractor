using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Web.Tests;

/// <summary>
/// Where a game in play stands, in the words the Game window's title uses: the period and clock
/// as a tag ("Q3 4:12"), and the situation as one line under it ("CHI ball, 2nd &amp; 6 at NE 34").
/// Nothing is said for a field ESPN did not send, and nothing at all once the game is over.
/// </summary>
public class PlayStateTests
{
    private static Game Football(GameStatus status = GameStatus.Live) => new()
    {
        Sport = new Sport { Key = "nfl" },
        HomeTeamId = 1,
        HomeTeam = new Team { Id = 1, Name = "New England Patriots", Abbrev = "NE" },
        AwayTeamId = 2,
        AwayTeam = new Team { Id = 2, Name = "Chicago Bears", Abbrev = "CHI" },
        Status = status
    };

    [Theory]
    [InlineData(3, "4:12", "4:12 - 3rd", "Q3 4:12")]
    [InlineData(2, "0:00", "Halftime", "Halftime")]
    [InlineData(3, "0:00", "End of 3rd", "End of 3rd")]
    [InlineData(5, "7:41", "7:41 - OT", "OT 7:41")]
    [InlineData(6, "2:00", null, "2OT 2:00")]
    [InlineData(1, "15:00", null, "Q1 15:00")]
    public void A_football_clock_reads_quarter_then_time(int period, string clock, string? detail, string expected)
    {
        var game = Football();
        game.Period = period;
        game.DisplayClock = clock;
        game.StatusDetail = detail;

        Assert.Equal(expected, PlayState.Clock(game));
    }

    [Fact]
    public void A_game_with_no_clock_says_nothing_and_a_finished_one_neither()
    {
        Assert.Null(PlayState.Clock(Football()));

        var final = Football(GameStatus.Final);
        final.Period = 4;
        final.DisplayClock = "0:00";
        final.StatusDetail = "Final/OT";
        Assert.Null(PlayState.Clock(final));
        Assert.Null(PlayState.Situation(final));
    }

    [Fact]
    public void Baseball_reads_the_half_inning_as_espn_prints_it()
    {
        var game = Football();
        game.Sport = new Sport { Key = "mlb" };
        game.Period = 7;
        game.StatusDetail = "Top 7th";

        Assert.Equal("Top 7th", PlayState.Clock(game));
    }

    [Fact]
    public void A_fight_reads_round_then_time()
    {
        var game = Football();
        game.Sport = new Sport { Key = "ufc" };
        game.Period = 2;
        game.DisplayClock = "3:14";
        game.StatusDetail = "R2, 3:14";

        Assert.Equal("R2 3:14", PlayState.Clock(game));
        Assert.Null(PlayState.Situation(game));
    }

    [Fact]
    public void The_football_situation_names_the_side_with_the_ball_and_the_spot()
    {
        var game = Football();
        game.PossessionTeamId = 2;
        game.DownDistanceText = "2nd & 6 at NE 34";

        Assert.Equal("CHI ball, 2nd & 6 at NE 34", PlayState.Situation(game));

        game.DownDistanceText = null;
        Assert.Equal("CHI ball", PlayState.Situation(game));

        game.PossessionTeamId = null;
        Assert.Null(PlayState.Situation(game));
    }

    [Theory]
    [InlineData("{\"balls\":1,\"strikes\":2,\"outs\":2,\"onFirst\":true,\"onSecond\":false,\"onThird\":true}", "2 out · 1-2 · runners on 1st, 3rd")]
    [InlineData("{\"balls\":0,\"strikes\":0,\"outs\":0,\"onFirst\":false,\"onSecond\":false,\"onThird\":false}", "0 out · 0-0 · bases empty")]
    [InlineData("{\"outs\":1,\"onSecond\":true}", "1 out · runner on 2nd")]
    [InlineData("{\"outs\":1,\"onFirst\":true,\"onSecond\":true,\"onThird\":true}", "1 out · bases loaded")]
    [InlineData("not json", null)]
    [InlineData("{}", null)]
    public void The_baseball_situation_is_outs_count_and_runners(string situation, string? expected)
    {
        var game = Football();
        game.Sport = new Sport { Key = "mlb" };
        game.Situation = situation;

        Assert.Equal(expected, PlayState.Situation(game));
    }

    [Fact]
    public void A_long_last_play_is_cut_at_a_word()
    {
        var game = Football();
        game.LastPlay = "(Shotgun) D.Maye pass incomplete short left to S.Diggs [J.Johnson]. Penalty on CHI-T.Edmunds, Defensive Holding, 5 yards, enforced at NE 34 - No Play.";

        var last = PlayState.LastPlay(game, max: 60)!;

        Assert.True(last.Length <= 61);
        Assert.EndsWith("…", last);
        Assert.StartsWith("(Shotgun) D.Maye pass incomplete", last);

        game.LastPlay = "Kneel down.";
        Assert.Equal("Kneel down.", PlayState.LastPlay(game));

        game.Status = GameStatus.Final;
        Assert.Null(PlayState.LastPlay(game));
    }
}
