using LineOps.Ingestion.Services;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The Worker's <c>--job espn:range</c> arguments: a league, two ISO dates and an optional
/// <c>--refresh</c>. A bad one is named, so the usage printed beside it says what to fix.
/// </summary>
public class RangePullArgumentsTests
{
    [Fact]
    public void A_league_and_two_dates_make_a_range()
    {
        Assert.True(RangePullRequest.TryParse("nfl", "2024-09-01", "2024-09-30", null, out var request, out var error));

        Assert.Null(error);
        Assert.Equal(new RangePullRequest("nfl", new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 30)), request);
    }

    [Fact]
    public void Refresh_is_read_as_a_boolean()
    {
        Assert.True(RangePullRequest.TryParse("mlb", "2025-04-01", "2025-04-30", "true", out var request, out _));

        Assert.True(request!.Refresh);
    }

    [Theory]
    [InlineData(null, "2024-09-01", "2024-09-30", null, "Name a league")]
    [InlineData("nfl", null, "2024-09-30", null, "--from is missing")]
    [InlineData("nfl", "2024-09-01", "30/09/2024", null, "--to '30/09/2024' is not a date")]
    [InlineData("nfl", "2024-09-01", "2024-09-30", "yes", "--refresh 'yes'")]
    [InlineData("nfl", "2024-09-30", "2024-09-01", null, "is after")]
    [InlineData("mlb", "2025-01-01", "2025-12-31", null, "at most 200")]
    public void A_bad_argument_is_named(string? sport, string? from, string? to, string? refresh, string expected)
    {
        Assert.False(RangePullRequest.TryParse(sport, from, to, refresh, out var request, out var error));

        Assert.Null(request);
        Assert.Contains(expected, error);
    }
}
