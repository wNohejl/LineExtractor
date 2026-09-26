namespace LineOps.Core.Analytics;

/// <summary>
/// What a season is called, and when one starts, per sport.
///
/// <para>
/// The provider is the authority on which season a game belongs to — ESPN stamps every event
/// with a year and a type, and <c>EntityResolver</c> writes that onto the game. This class is
/// what stands in when the provider has not spoken: rows written before the column existed,
/// a source that carries no season block, and the question of where a backfill should start.
/// </para>
///
/// <para>
/// Pure and static, so the rules are testable without a database and cannot drift between
/// the migration that back-filled the column and the code that fills it going forward.
/// </para>
/// </summary>
public static class SeasonCalendar
{
    /// <summary>
    /// The season a game on this date belongs to, by the year the sport names it.
    ///
    /// <para>
    /// Baseball is a calendar-year sport: opening day and the World Series share a year.
    /// Football is not: the 2025 NFL season runs from September 2025 to the Super Bowl in
    /// February 2026, and every one of those games is season 2025. Basketball and hockey
    /// straddle the year the same way, starting in October.
    /// </para>
    /// </summary>
    public static int YearOf(string sportKey, DateTimeOffset startsAt)
    {
        // The league's own day, not UTC's: a 9pm Eastern game on 30 September is a
        // September game, whatever the date in Greenwich (see LeagueClock).
        var local = LeagueClock.LocalTime(startsAt);

        return Normalise(sportKey) switch
        {
            "nfl" or "ncaaf" => local.Month < 3 ? local.Year - 1 : local.Year,
            "nba" or "nhl" or "ncaab" => local.Month < 9 ? local.Year - 1 : local.Year,
            _ => local.Year
        };
    }

    /// <summary>
    /// The part of the season a game on this date falls in, by rule — used only where the
    /// provider gave no stamp. Deliberately conservative: the boundaries are the ones that
    /// have held for years, not the ones that could move.
    /// </summary>
    public static Entities.SeasonType TypeOf(string sportKey, DateTimeOffset startsAt)
    {
        var local = LeagueClock.LocalTime(startsAt);

        return Normalise(sportKey) switch
        {
            // The regular season is eighteen weeks from a kickoff on the Thursday after Labor
            // Day, so its last game is the Monday of week 18 — 5 January for the 2025 season,
            // 11 January for 2026. A fixed day of the month cannot follow a calendar that
            // shifts a week each year; the league's own rule can.
            "nfl" => DateOnly.FromDateTime(local) > NflRegularSeasonEnd(YearOf("nfl", startsAt))
                ? Entities.SeasonType.Postseason
                : Entities.SeasonType.Regular,

            // The regular season ends in the last days of September; October is the playoffs.
            "mlb" => local.Month >= 10
                ? Entities.SeasonType.Postseason
                : Entities.SeasonType.Regular,

            // Playoffs run mid-April to June.
            "nba" or "nhl" => local.Month is >= 4 and <= 6 && !(local.Month == 4 && local.Day < 15)
                ? Entities.SeasonType.Postseason
                : Entities.SeasonType.Regular,

            _ => Entities.SeasonType.Regular
        };
    }

    /// <summary>
    /// How the season is written on the desk. Sports that straddle the year are labelled by
    /// both halves, the way their leagues write them; the rest by the year alone.
    /// </summary>
    public static string Label(string sportKey, int seasonYear)
        => Normalise(sportKey) switch
        {
            "nba" or "nhl" or "ncaab" => $"{seasonYear}–{(seasonYear + 1) % 100:00}",
            _ => seasonYear.ToString()
        };

    /// <summary>
    /// The current season for a sport, as of a given moment — the one a fresh window should
    /// open on. Follows the same year rule as <see cref="YearOf"/>.
    /// </summary>
    public static int CurrentYear(string sportKey, DateTimeOffset now) => YearOf(sportKey, now);

    /// <summary>
    /// The NFL's kickoff for a season: the Thursday after Labor Day, the first Monday of
    /// September. 4 September 2025; 10 September 2026.
    /// </summary>
    public static DateOnly NflKickoff(int seasonYear)
    {
        var first = new DateOnly(seasonYear, 9, 1);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(daysToMonday + 3);
    }

    /// <summary>
    /// The end of the NFL regular season: the Monday of week 18, seventeen weeks and four days
    /// after kickoff. Anything later in that season is the postseason. A league date, so the
    /// Monday night game that kicks off at 8:15pm Eastern — already Tuesday in UTC — is week 18.
    /// </summary>
    public static DateOnly NflRegularSeasonEnd(int seasonYear)
        => NflKickoff(seasonYear).AddDays(17 * 7 + 4);

    /// <summary>
    /// How many games a season part is scheduled to hold, where the league fixes it — the
    /// "expected" a coverage report holds the data against.
    ///
    /// <para>
    /// MLB: thirty clubs, 162 games each, 2,430. The NFL: seventeen games a club since 2021
    /// (272), sixteen before (256); a postseason of thirteen since the 2020 season widened the
    /// field to fourteen, eleven before. MLB's postseason length depends on how many series go
    /// the distance, so it has no expectation rather than a guessed one — nor does a league or
    /// season part not named here.
    /// </para>
    /// </summary>
    public static int? ExpectedGames(string sportKey, int seasonYear, Entities.SeasonType type)
        => (Normalise(sportKey), type) switch
        {
            ("mlb", Entities.SeasonType.Regular) => 2430,
            ("nfl", Entities.SeasonType.Regular) => seasonYear >= 2021 ? 272 : 256,
            ("nfl", Entities.SeasonType.Postseason) => seasonYear >= 2020 ? 13 : 11,
            _ => null
        };

    private static string Normalise(string sportKey) => sportKey.Trim().ToLowerInvariant();
}
