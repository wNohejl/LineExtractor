using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Covers the incident record: promotion from an alert, the running timeline, and the rule that
/// an incident cannot be closed without a written root cause and a corrective action.
///
/// That last rule is the whole point of the incident log — enforced in the service rather than
/// asked for in the UI, so it holds no matter who is calling. Which makes it worth pinning:
/// nothing else in the codebase notices if the enforcement is lost.
/// </summary>
[Collection(PostgresCollection.Name)]
public class IncidentServiceTests(PostgresFixture fixture)
{
    private static IncidentService Create(LineOpsDbContext db)
        => new(db, NullLogger<IncidentService>.Instance);

    private async Task<Alert> NewAlertAsync(
        LineOpsDbContext db, AlertSeverity severity = AlertSeverity.Critical)
    {
        var source = new Source
        {
            Key = $"test-{Guid.NewGuid():N}",
            Name = "Test source",
            Kind = SourceKind.Odds,
            BaseUrl = "local://test",
            Enabled = true
        };

        db.Sources.Add(source);
        await db.SaveChangesAsync();

        var alert = new Alert
        {
            RuleKey = AlertRules.Freshness,
            SourceId = source.Id,
            Severity = severity,
            Message = "Test source: no successful ingestion for 30.0h (SLO 26h).",
            TriggeredAt = DateTimeOffset.UtcNow
        };

        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    }

    [Fact]
    public async Task PromotingAnAlertOpensAnIncidentAndLinksItBack()
    {
        await using var db = fixture.CreateContext();
        var alert = await NewAlertAsync(db);

        var incident = await Create(db).PromoteAsync(alert.Id);

        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(alert.Severity, incident.Severity);

        // The link is what lets the alert feed point at the incident it became.
        var reloaded = await db.Alerts.FirstAsync(a => a.Id == alert.Id);
        Assert.Equal(incident.Id, reloaded.IncidentId);
    }

    [Fact]
    public async Task PromotingTheSameAlertTwiceReturnsTheSameIncident()
    {
        await using var db = fixture.CreateContext();
        var alert = await NewAlertAsync(db);
        var service = Create(db);

        var first = await service.PromoteAsync(alert.Id);
        var second = await service.PromoteAsync(alert.Id);

        // The evaluator can promote on every tick while a critical persists, and an operator can
        // click Promote on an alert that already has an incident. Neither may open a duplicate.
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Incidents.CountAsync(i => i.Id == first.Id));
    }

    [Fact]
    public async Task TheOpeningTimelineEntryRecordsWhichAlertCausedIt()
    {
        await using var db = fixture.CreateContext();
        var alert = await NewAlertAsync(db);

        var incident = await Create(db).PromoteAsync(alert.Id);
        var timeline = IncidentService.Deserialise(incident.Timeline);

        var opening = Assert.Single(timeline);
        Assert.Contains(alert.RuleKey, opening.Note);
    }

    [Fact]
    public async Task ResolvingWithoutARootCauseIsRefused()
    {
        await using var db = fixture.CreateContext();
        var incident = await Create(db).PromoteAsync((await NewAlertAsync(db)).Id);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Create(db).ResolveAsync(incident.Id, "   ", "Pinned the payload as a fixture."));

        // And the incident must still be open — a refused resolve that half-applied would be worse
        // than one that threw.
        var reloaded = await db.Incidents.FirstAsync(i => i.Id == incident.Id);
        Assert.Equal(IncidentStatus.Open, reloaded.Status);
        Assert.Null(reloaded.ResolvedAt);
    }

    [Fact]
    public async Task ResolvingWithoutACorrectiveActionIsRefused()
    {
        await using var db = fixture.CreateContext();
        var incident = await Create(db).PromoteAsync((await NewAlertAsync(db)).Id);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Create(db).ResolveAsync(incident.Id, "Provider changed the odds payload shape.", ""));

        var reloaded = await db.Incidents.FirstAsync(i => i.Id == incident.Id);
        Assert.Equal(IncidentStatus.Open, reloaded.Status);
    }

    [Fact]
    public async Task ResolvingWithBothRecordsTheAnalysisAndStopsTheClock()
    {
        await using var db = fixture.CreateContext();
        var incident = await Create(db).PromoteAsync((await NewAlertAsync(db)).Id);

        await Create(db).ResolveAsync(
            incident.Id,
            "Provider changed the odds payload shape; the adapter parsed zero rows.",
            "Recorded the new payload as a fixture, then fixed the parser. See commit abc1234.");

        var reloaded = await db.Incidents.FirstAsync(i => i.Id == incident.Id);

        Assert.Equal(IncidentStatus.Resolved, reloaded.Status);
        Assert.NotNull(reloaded.ResolvedAt);
        Assert.NotNull(reloaded.RootCause);
        Assert.NotNull(reloaded.CorrectiveActions);
        Assert.NotNull(reloaded.TimeToResolve);
    }

    [Fact]
    public async Task TimelineNotesAccumulateInOrderAcrossSeparateContexts()
    {
        await using var db = fixture.CreateContext();
        var incident = await Create(db).PromoteAsync((await NewAlertAsync(db)).Id);

        await Create(db).AddNoteAsync(incident.Id, "Confirmed the provider is returning HTTP 200.");
        await Create(db).AddNoteAsync(incident.Id, "Compared against the recorded fixture.");

        var reloaded = await db.Incidents.FirstAsync(i => i.Id == incident.Id);
        var timeline = IncidentService.Deserialise(reloaded.Timeline);

        // Opening entry plus both notes, still in the order they were written — the timeline is
        // stored as jsonb, so this also pins the round-trip.
        Assert.Equal(3, timeline.Count);
        Assert.Contains("HTTP 200", timeline[1].Note);
        Assert.Contains("fixture", timeline[2].Note);
        Assert.True(timeline[2].At >= timeline[0].At);
    }

    [Fact]
    public async Task AMalformedTimelineDegradesToEmptyRatherThanThrowing()
    {
        // The timeline is the one free-form column in the schema. A panel that throws while
        // rendering an incident would take away the tool you use during an incident.
        Assert.Empty(IncidentService.Deserialise("not json"));
        Assert.Empty(IncidentService.Deserialise(""));
    }

    [Fact]
    public async Task MarkingMitigatedIsRecordedWithoutClosingTheIncident()
    {
        await using var db = fixture.CreateContext();
        var incident = await Create(db).PromoteAsync((await NewAlertAsync(db)).Id);

        await Create(db).SetStatusAsync(incident.Id, IncidentStatus.Mitigated);

        var reloaded = await db.Incidents.FirstAsync(i => i.Id == incident.Id);

        // Mitigated means the bleeding stopped, not that the RCA is written. The incident still
        // owes one, so it must not look closed.
        Assert.Equal(IncidentStatus.Mitigated, reloaded.Status);
        Assert.Null(reloaded.ResolvedAt);
        Assert.Null(reloaded.RootCause);
        Assert.Contains(IncidentService.Deserialise(reloaded.Timeline),
            n => n.Note.Contains("Mitigated"));
    }

    [Fact]
    public async Task PromotingAnAlertThatDoesNotExistThrows()
    {
        await using var db = fixture.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(db).PromoteAsync(alertId: -1));
    }
}
