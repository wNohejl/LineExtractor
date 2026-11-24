using XLines.Ingestion.Services;

namespace XLines.Tests.Reliability;

/// <summary>
/// Where the windows and the gamecast feed meet: a window's interest in a game is a lease, counted
/// per window, and the feed's last poll is a snapshot that stays null where the feed has not run.
/// </summary>
public class GamecastLanesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_watched_game_is_watched_until_its_lease_runs_out()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);

        Assert.Equal([7], lanes.Watched(Now));
        Assert.Equal([7], lanes.Watched(Now + GamecastLanes.Lease - TimeSpan.FromSeconds(1)));
        Assert.Empty(lanes.Watched(Now + GamecastLanes.Lease));

        // Dropped, not just hidden: an earlier clock does not bring it back.
        Assert.Empty(lanes.Watched(Now));
    }

    [Fact]
    public void Watching_again_renews_the_lease()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);
        lanes.Watch(7, Now.AddSeconds(60));

        Assert.Equal([7], lanes.Watched(Now + GamecastLanes.Lease));
    }

    [Fact]
    public void Renewing_extends_the_lease_without_counting_the_window_again()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);
        lanes.Renew(7, Now.AddSeconds(60));
        lanes.Renew(7, Now.AddSeconds(120));

        // The lease runs from the last renewal.
        Assert.Equal([7], lanes.Watched(Now.AddSeconds(120) + GamecastLanes.Lease - TimeSpan.FromSeconds(1)));

        // One watch, one let-go: nothing is left behind however often it was renewed.
        lanes.Unwatch(7);
        Assert.Empty(lanes.Watched(Now.AddSeconds(120)));
    }

    [Fact]
    public void Renewing_a_game_nobody_watches_starts_nothing()
    {
        var lanes = new GamecastLanes();
        lanes.Renew(7, Now);

        Assert.Empty(lanes.Watched(Now));
    }

    [Fact]
    public void Renewing_after_the_lease_ran_out_does_not_bring_the_game_back()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);

        // Not yet swept by a read of the watched games, but past its lease all the same.
        lanes.Renew(7, Now + GamecastLanes.Lease);

        Assert.Empty(lanes.Watched(Now + GamecastLanes.Lease));
    }

    [Fact]
    public void Renewing_one_game_leaves_another_alone()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);
        lanes.Watch(8, Now);

        lanes.Renew(7, Now.AddSeconds(60));

        Assert.Equal([7], lanes.Watched(Now + GamecastLanes.Lease));
    }

    [Fact]
    public void One_window_letting_go_does_not_cut_off_another_still_watching()
    {
        var lanes = new GamecastLanes();
        lanes.Watch(7, Now);
        lanes.Watch(7, Now);

        lanes.Unwatch(7);
        Assert.Equal([7], lanes.Watched(Now));

        lanes.Unwatch(7);
        Assert.Empty(lanes.Watched(Now));

        // One too many is harmless.
        lanes.Unwatch(7);
        Assert.Empty(lanes.Watched(Now));
    }

    [Fact]
    public void The_snapshot_is_null_until_the_feed_reports_and_says_each_games_lane_after()
    {
        var lanes = new GamecastLanes();

        Assert.Null(lanes.Snapshot);
        Assert.Null(lanes.LaneOf(1));

        lanes.Report(new GamecastFeedSnapshot(Now, InPlay: 2, FastLaneSize: 1,
            new Dictionary<int, GamecastLane> { [1] = GamecastLane.Fast, [2] = GamecastLane.Slow },
            new Dictionary<int, DateTimeOffset> { [1] = Now }));

        Assert.Equal(GamecastLane.Fast, lanes.LaneOf(1));
        Assert.Equal(GamecastLane.Slow, lanes.LaneOf(2));
        Assert.Null(lanes.LaneOf(3));
        Assert.Equal(2, lanes.Snapshot!.InPlay);
    }

    [Fact]
    public async Task Windows_and_the_feed_can_use_it_at_once()
    {
        var lanes = new GamecastLanes();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 1_000; i++)
            {
                lanes.Watch(i % 10, Now);
                _ = lanes.Watched(Now);
                lanes.Unwatch(i % 10);
            }
        })));

        Assert.Empty(lanes.Watched(Now));
    }
}
