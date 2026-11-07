using System.Globalization;
using System.Text.Json;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Adapters;

public partial class EspnStatsAdapter
{
    /// <summary>
    /// The days of a range with games on them, from the core API's season calendar.
    ///
    /// <para>
    /// The site scoreboard will not take a range — <c>dates=20251001-20251031</c> answers
    /// <c>{"code":400}</c> (measured 7 November 2026) — so a range pull either asks it every day or
    /// learns the game days first. <c>calendar/ondays?dates={year}</c> states one season's game
    /// days in one small call, for MLB, the NFL and the UFC alike: an NFL season runs from its
    /// August preseason to the February final, MLB's from spring training to the World Series, and
    /// the UFC's is the calendar year, Contender Series Tuesdays included. One request a season
    /// year the range touches, and one more for the season before when the range starts ahead of
    /// the first one read: a January range is the previous NFL season's playoffs.
    /// </para>
    ///
    /// <para>
    /// A calendar that will not answer, or answers in a shape not seen before, is not trusted with
    /// the range: every day stands in for it. Asking an empty day costs a request; believing a
    /// misread calendar would cost the games on the days it left out.
    /// </para>
    /// </summary>
    public async Task<GameDays> FetchGameDaysAsync(string sportKey, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || MapPath(sportKey) is not { } path || path.Split('/') is not [var sport, var league])
            return new GameDays([], false, new FetchCost(0));

        var every = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var calendars = new List<SeasonCalendar>();
        var requests = 0;

        async Task<bool> ReadAsync(int year)
        {
            requests++;

            try
            {
                using var doc = await GetJsonAsync(
                    $"{CoreApi}{sport}/leagues/{league}/calendar/ondays?dates={year}", ct);

                if (ParseCalendar(doc.RootElement) is not { } calendar)
                {
                    logger.LogWarning("{Source}: {Sport} calendar for {Year} is not in a shape we know; walking every day",
                        SourceKey, sportKey, year);
                    return false;
                }

                calendars.Add(calendar);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Source}: {Sport} calendar for {Year} unavailable; walking every day",
                    SourceKey, sportKey, year);
                return false;
            }
        }

        for (var year = from.Year; year <= to.Year; year++)
        {
            if (!await ReadAsync(year))
                return new GameDays(every, false, new FetchCost(requests));
        }

        if (from < calendars.Min(c => c.Start) && !await ReadAsync(from.Year - 1))
            return new GameDays(every, false, new FetchCost(requests));

        var held = calendars.SelectMany(c => c.Days).ToHashSet();

        return new GameDays(every.Where(held.Contains).ToList(), true, new FetchCost(requests));
    }

    /// <summary>One season's calendar: the window it covers and the league days in it with games.</summary>
    internal readonly record struct SeasonCalendar(DateOnly Start, DateOnly End, IReadOnlyList<DateOnly> Days);

    /// <summary>
    /// <c>eventDate.dates[]</c>, ISO instants at midnight Pacific (<c>2024-09-05T07:00Z</c>), each
    /// read as the league day it falls on (<see cref="LeagueClock"/>) — the day the scoreboard is
    /// addressed by. Null for any shape other than an array of readable instants, which is what
    /// sends the caller to every day.
    /// </summary>
    internal static SeasonCalendar? ParseCalendar(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("eventDate", out var eventDate)
            || eventDate.ValueKind != JsonValueKind.Object
            || !eventDate.TryGetProperty("dates", out var dates)
            || dates.ValueKind != JsonValueKind.Array)
            return null;

        var days = new List<DateOnly>();

        foreach (var date in dates.EnumerateArray())
        {
            if (Instant(date) is not { } instant)
                return null;

            days.Add(LeagueClock.DateOf(instant));
        }

        if (days.Count == 0)
            return null;

        days = days.Distinct().Order().ToList();

        // The window, where ESPN states it; the days themselves where it does not.
        var start = root.TryGetProperty("startDate", out var s) && Instant(s) is { } begins ? LeagueClock.DateOf(begins) : days[0];
        var end = root.TryGetProperty("endDate", out var e) && Instant(e) is { } ends ? LeagueClock.DateOf(ends) : days[^1];

        return new SeasonCalendar(start < days[0] ? start : days[0], end > days[^1] ? end : days[^1], days);
    }

    private static DateTimeOffset? Instant(JsonElement value)
        => value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? instant
            : null;
}
