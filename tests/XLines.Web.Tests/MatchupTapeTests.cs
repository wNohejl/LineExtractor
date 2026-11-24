using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Snippets;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Board row's Form answer for a team sport: the two clubs side by side, where each market
/// stands and where its best book opened, and each club's season — every figure from the rows the
/// database already holds, bounded by the game's season, and nothing where there is nothing.
/// </summary>
public class MatchupTapeTests : DeskTestContext
{
    private static readonly DateTimeOffset Kickoff = new(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

    private static TeamGameLogRow Final(int daysBefore, string opponent, bool home, int scored, int allowed,
        decimal? spread = null, EntryResult? ats = null, decimal? total = null, EntryResult? overUnder = null, string? abbrev = null)
        => new(100 + daysBefore, Kickoff.AddDays(-daysBefore), opponent, home, scored, allowed, GameStatus.Final,
            spread, ats, total, overUnder, "nfl", abbrev);

    // ---- A club's season ------------------------------------------------------------------

    [Fact]
    public void A_clubs_season_is_its_record_where_it_was_won_what_it_scores_and_its_records_against_the_lines()
    {
        IReadOnlyList<TeamGameLogRow> season =
        [
            Final(6, "Dallas Cowboys", home: true, 27, 20, -3m, EntryResult.Win, 44.5m, EntryResult.Win, "DAL"),
            Final(13, "New York Giants", home: false, 17, 24, 2.5m, EntryResult.Loss, 41m, EntryResult.Push),
            Final(20, "Philadelphia Eagles", home: true, 30, 10)
        ];

        var club = MatchupTape.Club(season, Kickoff, ("QB", "Allen", "Josh Allen"));

        Assert.Equal(3, club.Played);
        Assert.Equal("2-1", club.Record);
        Assert.Equal("2-0 home · 0-1 away", club.HomeAway);
        Assert.Equal("24.7 – 18.0", club.PerGame);
        Assert.Equal("1-1 · 2 of 3 lined", club.Ats);
        Assert.Equal("1-0-1 · 2 of 3 lined", club.OverUnder);
        Assert.Equal("6 days", club.Rest);
        Assert.Equal("W 27-20 vs DAL", club.LastGame);
        Assert.Equal("QB Allen", club.Starter);
    }

    /// <summary>A record with no closing line behind it is "no lines", not 0-0: never priced is not never covered.</summary>
    [Fact]
    public void A_season_with_no_closing_lines_says_so_and_one_with_no_finals_says_none()
    {
        var unpriced = MatchupTape.Club([Final(7, "Miami Dolphins", home: false, 21, 14)], Kickoff, null);
        Assert.Equal("no lines", unpriced.Ats);
        Assert.Equal("no lines", unpriced.OverUnder);

        var none = MatchupTape.Club([], Kickoff, null);
        Assert.Equal((0, "—", "first game this season", null), (none.Played, none.Record, none.Rest, none.LastGame));
    }

    /// <summary>Rest is counted in league days, not elapsed hours floored.</summary>
    [Fact]
    public void Rest_is_league_days_apart_across_midnight()
    {
        // A night game (11pm ET, 04:00 UTC the next day) then the next afternoon's 1pm ET game.
        var night = new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero);
        var afternoon = new DateTimeOffset(2026, 7, 11, 17, 0, 0, TimeSpan.Zero);
        var last = new TeamGameLogRow(1, night, "New York Mets", true, 4, 3, GameStatus.Final, null, null, null, null);
        Assert.Equal("1 day", MatchupTape.Club([last], afternoon, null).Rest);

        // Sunday night to the next Sunday's 1pm is seven days, not six.
        var snf = new DateTimeOffset(2026, 11, 16, 1, 20, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);
        var sundayNight = new TeamGameLogRow(2, snf, "Dallas Cowboys", true, 24, 20, GameStatus.Final, null, null, null, null);
        Assert.Equal("7 days", MatchupTape.Club([sundayNight], sunday, null).Rest);

        // A doubleheader is the same day.
        var opener = new TeamGameLogRow(3, afternoon, "New York Mets", true, 2, 1, GameStatus.Final, null, null, null, null);
        Assert.Equal("same day", MatchupTape.Club([opener], afternoon.AddHours(4), null).Rest);
    }

    [Fact]
    public void A_record_against_every_finals_line_is_said_bare()
        => Assert.Equal("1-0", MatchupTape.Club([Final(7, "Miami Dolphins", home: false, 21, 14, 3m, EntryResult.Win)], Kickoff, null).Ats);

    // ---- The lines ----------------------------------------------------------------------

    private static OddsSnapshot Snap(string market, string outcome, string book, int price, decimal? line, int minutesAgo)
        => new()
        {
            GameId = BoardRows.GameId, Market = market, Outcome = outcome, Book = book, PriceAmerican = price, Line = line,
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)
        };

    [Fact]
    public void Each_market_reads_away_first_with_the_best_book_where_it_opened_fair_and_hold()
    {
        var row = BoardRows.Row();
        var markets = MatchupTape.Markets(row,
        [
            Snap(Markets.Moneyline, BoardRows.Home, "draftkings", -120, null, 300),
            Snap(Markets.Moneyline, BoardRows.Home, "draftkings", -128, null, 12),
            Snap(Markets.Spread, BoardRows.Away, "fanduel", -105, 2.5m, 60)
        ]);

        Assert.Equal(["Moneyline", "Spread", "Total"], markets.Select(m => m.Name));

        var ml = markets[0];
        Assert.Equal(("+110", "FanDuel"), (ml.Leading!.Quote, ml.Leading.Book));
        Assert.Equal(("-128", "DraftKings", "opened -120", "+2.7%"),
            (ml.Trailing!.Quote, ml.Trailing.Book, ml.Trailing.Opened, ml.Trailing.Edge));
        Assert.StartsWith("fair Chiefs -136", ml.Detail);

        var spread = markets[1];
        Assert.Equal(("+2.5 -105", "unmoved"), (spread.Leading!.Quote, spread.Leading.Opened));

        // The over leads a total, as it is said.
        Assert.StartsWith("o47.5", markets[2].Leading!.Quote);
        Assert.Null(markets[2].Leading!.Opened);
    }

    [Fact]
    public void A_market_no_book_priced_has_no_line_and_a_closed_one_says_it_is_the_close()
    {
        var row = BoardRows.Row() with { Spread = new MarketPair(Markets.Spread, null, null) };
        var spread = MatchupTape.Markets(row, [])[1];
        Assert.Null(spread.Leading);
        Assert.Null(spread.Trailing);
        Assert.Null(spread.Detail);

        var closed = MatchupTape.Markets(BoardRows.Row(closing: true), [])[0];
        Assert.Equal("closing line", closed.Detail);
        Assert.Null(closed.Trailing!.Edge);
    }

    // ---- The answer, rendered --------------------------------------------------------

    private sealed class Reads : IMatchupData
    {
        public BoardRow? Row { get; set; } = BoardRows.Row() with
        {
            Spread = new MarketPair(Markets.Spread, null, null),
            HomeQuarterback = "Patrick Mahomes",
            AwayQuarterback = "Josh Allen"
        };

        public int RowReads { get; private set; }

        public Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
        {
            RowReads++;
            return Task.FromResult(Row);
        }

        public Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OddsSnapshot>>([]);

        public List<(int Team, int Year, SeasonType Type)> Seasons { get; } = [];

        public Task<IReadOnlyList<TeamGameLogRow>> SeasonAsync(int teamId, int seasonYear, SeasonType seasonType, CancellationToken ct = default)
        {
            Seasons.Add((teamId, seasonYear, seasonType));
            IReadOnlyList<TeamGameLogRow> log =
            [
                new(1, DateTimeOffset.UtcNow.AddDays(-7), "Denver Broncos", true, 24, 17, GameStatus.Final, -6m, EntryResult.Win, 45m,
                    EntryResult.Loss, "nfl", "DEN")
            ];
            return Task.FromResult(log);
        }
    }

    [Fact]
    public void The_form_answer_sets_the_clubs_side_by_side_over_the_lines_and_the_season()
    {
        var reads = new Reads();
        Services.AddSingleton<IMatchupData>(reads);

        var cut = RenderComponent<FormSnippet>(p => p.Add(x => x.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".mtape")));

        Assert.Equal("Lines", cut.FindAll(".mtape__group")[0].GetAttribute("aria-label"));
        Assert.StartsWith("The ", cut.FindAll(".mtape__group")[1].GetAttribute("aria-label"));
        Assert.Contains("QB Allen", cut.Find(".mtape__club").TextContent);
        Assert.Contains("QB Mahomes", cut.Find(".mtape__club--trailing").TextContent);

        // A market no book priced says so, on both sides.
        var spread = cut.FindAll(".mtape__group")[0].QuerySelectorAll(".mtape__row")[1];
        Assert.Equal(2, spread.QuerySelectorAll(".dim").Count(d => d.TextContent.Trim() == "no line"));

        var season = cut.FindAll(".mtape__group")[1].TextContent;
        Assert.Contains("Against the spread", season);
        Assert.Contains("W 24-17 vs DEN", season);
        Assert.NotEmpty(cut.FindAll(".mtape .formrun"));
    }

    /// <summary>The season is the bound: the game's own season year and season type, never a rolling window.</summary>
    [Fact]
    public void The_form_answer_reads_the_games_season_and_season_type_once_per_opened_row()
    {
        var reads = new Reads();
        var game = BoardRows.Game();
        game.SeasonYear = 2026;
        game.SeasonType = SeasonType.Postseason;
        reads.Row = reads.Row! with { Game = game };
        Services.AddSingleton<IMatchupData>(reads);

        var cut = RenderComponent<FormSnippet>(p => p.Add(x => x.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".mtape")));

        Assert.All(reads.Seasons, s => Assert.Equal((2026, SeasonType.Postseason), (s.Year, s.Type)));
        Assert.Contains("The 2026 postseason", cut.FindAll(".mtape__title")[1].TextContent);
        Assert.Equal("The 2026 postseason", cut.FindAll(".mtape__group")[1].GetAttribute("aria-label"));

        cut.SetParametersAndRender(p => p.Add(x => x.GameId, BoardRows.GameId));
        Assert.Equal(1, reads.RowReads);
    }
}
