using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using Microsoft.EntityFrameworkCore;

namespace XLines.Ingestion.Services;

/// <summary>
/// Writes what a provider says about a game's circumstances (<see cref="CanonicalGameContext"/>)
/// onto its <see cref="GameContext"/> row and its <see cref="GameOfficial"/> rows — the one path
/// both the stats pass (ESPN's summary) and the MLB spine (MLB's hydrated schedule) write through,
/// so the two cannot disagree about what "never erase" means.
///
/// <para>
/// The rules: a value stated replaces the value held; a value not stated leaves the held one
/// standing; a crew or a batting order is replaced whole by a fetch that names one and left alone
/// by a fetch that names nobody. A fetch that changes nothing writes nothing, so a re-walk of a day
/// already read leaves every row's <see cref="GameContext.UpdatedAt"/> where it was.
/// </para>
/// </summary>
public sealed class GameContextWriter(XLinesDbContext db)
{
    /// <summary>
    /// One game's context from one source. Saves nothing itself: the caller's save carries it,
    /// except a crew being replaced, whose old rows are deleted at once so the new ones can take
    /// their places in the crew's order.
    /// </summary>
    /// <returns>The rows written or changed: the context row, and each official written.</returns>
    public async Task<int> UpsertAsync(Game game, Source source, CanonicalGameContext canonical, CancellationToken ct)
    {
        var rows = 0;
        var now = DateTimeOffset.UtcNow;

        if (canonical.StatesRowFields)
        {
            var row = db.GameContexts.Local.FirstOrDefault(c => c.GameId == game.Id && c.SourceId == source.Id)
                      ?? await db.GameContexts.FirstOrDefaultAsync(c => c.GameId == game.Id && c.SourceId == source.Id, ct);

            var fresh = row is null;
            row ??= new GameContext { GameId = game.Id, SourceId = source.Id, CapturedAt = now };

            // A new row is added only once it holds something: a context of blank strings is no row.
            if (Apply(row, canonical))
            {
                row.UpdatedAt = now;
                rows++;

                if (fresh)
                    db.GameContexts.Add(row);
            }
        }

        if (canonical.Officials is { Count: > 0 } officials)
            rows += await ReplaceCrewAsync(game, source, officials, now, ct);

        return rows;
    }

    /// <summary>
    /// Every stated field onto the row, every unstated one left as held. Strings are trimmed to
    /// their columns so a provider's over-long words are cut rather than failing the save they ride in.
    /// </summary>
    /// <returns>True when anything changed.</returns>
    internal static bool Apply(GameContext row, CanonicalGameContext c)
    {
        var changed = false;

        Set(row.VenueName, Cut(c.VenueName, GameContext.VenueNameLength), v => row.VenueName = v);
        Set(row.VenueCity, Cut(c.VenueCity, GameContext.VenueCityLength), v => row.VenueCity = v);
        Set(row.VenueState, Cut(c.VenueState, GameContext.VenueStateLength), v => row.VenueState = v);
        Set(row.Indoor, c.Indoor, v => row.Indoor = v);
        Set(row.Surface, Cut(c.Surface, GameContext.SurfaceLength), v => row.Surface = v);
        Set(row.WeatherCondition, Cut(c.WeatherCondition, GameContext.WeatherConditionLength), v => row.WeatherCondition = v);
        Set(row.TemperatureF, c.TemperatureF, v => row.TemperatureF = v);
        Set(row.WindSpeedMph, c.WindSpeedMph, v => row.WindSpeedMph = v);
        Set(row.WindDirection, Cut(c.WindDirection, GameContext.WindDirectionLength), v => row.WindDirection = v);
        Set(row.PrecipitationPercent, c.PrecipitationPercent, v => row.PrecipitationPercent = v);
        Set(row.Attendance, c.Attendance, v => row.Attendance = v);
        Set(row.DayNight, Cut(c.DayNight, GameContext.DayNightLength), v => row.DayNight = v);

        // Rounded to the column's two places before comparing, or a re-read of a figure Postgres
        // holds rounded would read as a change every time.
        Set(row.HomeWinPercent, Round(c.HomeWinPercent), v => row.HomeWinPercent = v);
        Set(row.AwayWinPercent, Round(c.AwayWinPercent), v => row.AwayWinPercent = v);
        Set(row.MatchupQuality, Round(c.MatchupQuality), v => row.MatchupQuality = v);

        if (c.HomeLineup is { Count: > 0 } home && !SameLineup(row.HomeLineup, home))
        {
            row.HomeLineup = GameContext.SerializeLineup(home);
            changed = true;
        }

        if (c.AwayLineup is { Count: > 0 } away && !SameLineup(row.AwayLineup, away))
        {
            row.AwayLineup = GameContext.SerializeLineup(away);
            changed = true;
        }

        return changed;

        void Set<T>(T held, T stated, Action<T> write)
        {
            if (stated is null || EqualityComparer<T>.Default.Equals(held, stated))
                return;

            write(stated);
            changed = true;
        }
    }

    /// <summary>Compared as the slots, not as the jsonb text Postgres hands back re-spaced.</summary>
    private static bool SameLineup(string? stored, IReadOnlyList<CanonicalLineupSlot> stated)
        => GameContext.ParseLineup(stored).SequenceEqual(stated);

    /// <summary>
    /// The crew replaced by the one stated, when it differs from the one held. The order stored is
    /// the crew's position, first first — by the provider's own order where it gives one — so two
    /// officials the provider numbered alike cannot collide on the crew's key.
    /// </summary>
    private async Task<int> ReplaceCrewAsync(
        Game game, Source source, IReadOnlyList<CanonicalOfficial> officials, DateTimeOffset now, CancellationToken ct)
    {
        var stated = officials
            .Select((o, i) => (Official: o, Index: i))
            .Where(o => !string.IsNullOrWhiteSpace(o.Official.Name))
            .OrderBy(o => o.Official.Order ?? int.MaxValue)
            .ThenBy(o => o.Index)
            .Select((o, i) => (Order: i + 1,
                Name: Cut(o.Official.Name.Trim(), GameOfficial.NameLength)!,
                Role: Cut(o.Official.Role?.Trim(), GameOfficial.RoleLength)))
            .ToList();

        if (stated.Count == 0)
            return 0;

        var held = await db.GameOfficials
            .Where(o => o.GameId == game.Id && o.SourceId == source.Id)
            .OrderBy(o => o.Order)
            .ToListAsync(ct);

        if (held.Select(o => (o.Order, o.Name, o.Role)).SequenceEqual(stated))
            return 0;

        if (held.Count > 0)
        {
            await db.GameOfficials
                .Where(o => o.GameId == game.Id && o.SourceId == source.Id)
                .ExecuteDeleteAsync(ct);

            foreach (var gone in held)
                db.Entry(gone).State = EntityState.Detached;
        }

        db.GameOfficials.AddRange(stated.Select(o => new GameOfficial
        {
            GameId = game.Id,
            SourceId = source.Id,
            Order = o.Order,
            Name = o.Name,
            Role = o.Role,
            CapturedAt = now
        }));

        return stated.Count;
    }

    private static string? Cut(string? value, int max)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;

        return ColumnText.Fit(trimmed, max);
    }

    private static decimal? Round(decimal? value)
        => value is { } v ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;
}
