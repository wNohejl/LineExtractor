using System.Text.Json;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Reliability;

public record TimelineNote(DateTimeOffset At, string Note);

/// <summary>
/// Manages the incident record: promotion from an alert, the running timeline, and the
/// written root-cause analysis.
///
/// The discipline this enforces is the point of the whole project — an incident cannot be
/// closed without a root cause and a corrective action, so the history that accumulates is
/// a real RCA log rather than a list of things that broke.
/// </summary>
public class IncidentService(LineOpsDbContext db, ILogger<IncidentService> logger)
{
    public async Task<Incident> PromoteAsync(long alertId, string? title = null, CancellationToken ct = default)
    {
        var alert = await db.Alerts
            .Include(a => a.Source)
            .FirstOrDefaultAsync(a => a.Id == alertId, ct)
            ?? throw new InvalidOperationException($"Alert {alertId} not found.");

        if (alert.IncidentId is { } existingId)
            return await db.Incidents.FirstAsync(i => i.Id == existingId, ct);

        var incident = new Incident
        {
            Title = title ?? alert.Message,
            Severity = alert.Severity,
            Status = IncidentStatus.Open,
            OpenedAt = DateTimeOffset.UtcNow,
            Timeline = Serialise([
                new TimelineNote(DateTimeOffset.UtcNow,
                    $"Incident opened from alert [{alert.RuleKey}]: {alert.Message}")
            ])
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);

        alert.IncidentId = incident.Id;
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Incident #{Id} opened: {Title}", incident.Id, incident.Title);
        return incident;
    }

    public async Task AddNoteAsync(int incidentId, string note, CancellationToken ct = default)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new InvalidOperationException($"Incident {incidentId} not found.");

        var timeline = Deserialise(incident.Timeline).ToList();
        timeline.Add(new TimelineNote(DateTimeOffset.UtcNow, note));
        incident.Timeline = Serialise(timeline);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Closes an incident. Both a root cause and a corrective action are required —
    /// an incident without them teaches nothing and would leave the log decorative.
    /// </summary>
    public async Task ResolveAsync(
        int incidentId, string rootCause, string correctiveActions, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootCause))
            throw new ArgumentException("A root cause is required to resolve an incident.", nameof(rootCause));

        if (string.IsNullOrWhiteSpace(correctiveActions))
            throw new ArgumentException("A corrective action is required to resolve an incident.",
                nameof(correctiveActions));

        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new InvalidOperationException($"Incident {incidentId} not found.");

        incident.RootCause = rootCause;
        incident.CorrectiveActions = correctiveActions;
        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = DateTimeOffset.UtcNow;

        var timeline = Deserialise(incident.Timeline).ToList();
        timeline.Add(new TimelineNote(DateTimeOffset.UtcNow, "Resolved. Root cause and corrective actions recorded."));
        incident.Timeline = Serialise(timeline);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Incident #{Id} resolved after {Duration}", incident.Id, incident.TimeToResolve);
    }

    public async Task SetStatusAsync(int incidentId, IncidentStatus status, CancellationToken ct = default)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new InvalidOperationException($"Incident {incidentId} not found.");

        incident.Status = status;

        var timeline = Deserialise(incident.Timeline).ToList();
        timeline.Add(new TimelineNote(DateTimeOffset.UtcNow, $"Status changed to {status}."));
        incident.Timeline = Serialise(timeline);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Incident>> GetAllAsync(CancellationToken ct = default)
        => await db.Incidents
            .Include(i => i.Alerts)
            .OrderByDescending(i => i.OpenedAt)
            .ToListAsync(ct);

    public async Task<Incident?> GetAsync(int id, CancellationToken ct = default)
        => await db.Incidents
            .Include(i => i.Alerts).ThenInclude(a => a.Source)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public static IReadOnlyList<TimelineNote> Deserialise(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TimelineNote>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Serialise(IEnumerable<TimelineNote> notes)
        => JsonSerializer.Serialize(notes);
}
