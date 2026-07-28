namespace LineOps.Core.Contracts;

/// <summary>
/// A fixture already on the books, as an odds source needs to see it.
/// </summary>
public record ScheduledGame(
    string SportKey,
    string HomeTeamName,
    string AwayTeamName,
    DateTimeOffset StartsAt);

/// <summary>
/// Read access to the schedule for sources that price fixtures rather than discover them.
///
/// <para>
/// Real odds providers publish their own event list, so they need nothing from us. A
/// <i>simulator</i> is the opposite: it has no schedule of its own and must be told one, or it
/// invents fixtures — which is exactly what went wrong. The demo source shipped an eight-team
/// roster per sport and paired it up, so the desk held two disjoint sets of games: real ones
/// from ESPN with no prices, and invented ones with prices for teams that were not playing.
/// A board of mostly-fabricated rows looks populated, which is worse than looking empty.
/// </para>
///
/// <para>
/// This interface exists so the fixture can quote the real slate without <c>LineOps.Ingestion</c>
/// reaching into the database from inside an adapter. Implemented in the data layer.
/// </para>
/// </summary>
public interface IScheduleReader
{
    /// <summary>Games starting inside the window, for one sport.</summary>
    Task<IReadOnlyList<ScheduledGame>> GetUpcomingAsync(
        string sportKey, TimeSpan window, CancellationToken ct = default);
}
