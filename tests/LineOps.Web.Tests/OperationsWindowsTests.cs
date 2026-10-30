using System.Reflection;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Desk.Primitives;
using LineOps.Ingestion.Services;
using LineOps.Reliability;
using LineOps.Web.Components.Panels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The operations windows after the HIG pass (October 2026): Ops, Incidents, Runs and History.
///
/// Each read goes to Postgres through a service these tests do not stand up, so the panel's first
/// load fails and the test then places what a load would have read straight on the panel — what
/// is under test is how each window draws what it holds, not how it fetched it. The same pattern
/// as <see cref="IncidentTitleTests"/>.
/// </summary>
public class OperationsWindowsTests : DeskTestContext
{
    public OperationsWindowsTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(new UnreachableFactory());
        Services.AddSingleton<HistoryBackfillService>();
        Services.AddSingleton<BackfillCoordinator>();
    }

    /// <summary>A database that never answers: the load stays in flight, or fails, as the panel reads it.</summary>
    private sealed class UnreachableFactory : IDbContextFactory<LineOpsDbContext>
    {
        public LineOpsDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<LineOpsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new TaskCompletionSource<LineOpsDbContext>().Task;
    }

    private static void Set(object panel, string field, object? value)
    {
        for (var type = panel.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) is { } f)
            {
                f.SetValue(panel, value);
                return;
            }
        }

        throw new InvalidOperationException($"No field {field} on {panel.GetType().Name}.");
    }

    /// <summary>Marks the panel as having read what the test has placed on it.</summary>
    private static void Loaded(object panel)
    {
        typeof(PanelBase).GetProperty("LoadError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel, null);
        typeof(PanelBase).GetProperty("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel, true);
    }

    private static string Section(IRenderedFragment panel, string title)
        => panel.FindAll("section.psection")
            .Single(s => s.QuerySelector(".psection__head")!.TextContent.Trim() == title)
            .OuterHtml;

    // ---- Ops ---------------------------------------------------------------------------------

    private IRenderedComponent<OpsPanel> RenderOps(params OddsProviderState[] feeds)
    {
        var panel = RenderComponent<OpsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_feeds", feeds);
        Loaded(panel.Instance);
        panel.Render();
        return panel;
    }

    private static OddsProviderState LiveFeed()
        => new("theoddsapi", "The Odds API", IsLive: true, Enabled: true, ["draftkings", "fanduel"], "Configured and reachable.");

    [Fact]
    public void Ops_has_no_toolbar_band_and_its_key_leads_under_the_tiles()
    {
        var panel = RenderOps();

        Assert.Empty(panel.FindAll(".ptoolbar"));

        var key = panel.Find(".metrics + .opsact .desk-btn");
        Assert.Contains("Evaluate now", key.TextContent);
        Assert.Contains("desk-btn--filled", key.ClassName);
    }

    [Fact]
    public void Ops_with_no_open_alerts_says_so_without_an_empty_table()
    {
        var panel = RenderOps();

        var alerts = Section(panel, "Open alerts");
        Assert.Contains("No open alerts. Every source is inside its SLO.", alerts);
        Assert.DoesNotContain("deskgrid", alerts);
    }

    [Fact]
    public void Ops_scanning_is_a_section_with_its_cost_as_footer()
    {
        var panel = RenderOps(LiveFeed());

        var section = panel.Find("section.wsection[aria-label=Scanning]");
        var row = section.QuerySelector(".wgroup .wgroup__row--form")!;
        Assert.Equal("Scanning", row.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.NotNull(row.QuerySelector("[role=radiogroup]"));

        // The footer is the section's, directly under the group it prices.
        var footer = section.QuerySelector(".wgroup + .wgroup__foot")!;
        Assert.Contains("Lines are pulled on request only", footer.TextContent);
        Assert.Contains("Set by configuration until it is chosen here.", footer.TextContent);

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();

        var keys = panel.FindAll(".opsconfirm button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Scan automatically", "Cancel"], keys);
    }

    [Fact]
    public void Ops_drill_faults_are_a_section_with_what_the_drill_does_as_footer()
    {
        var panel = RenderComponent<OpsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_injectable", new List<Source> { new() { Id = 1, Key = "theoddsapi", Name = "The Odds API" } });
        Loaded(panel.Instance);
        panel.Render();

        var faults = panel.Find("section.wsection[aria-label=Faults]");
        Assert.Equal("The Odds API", faults.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.Contains("Inject a fault, run an ingest", faults.QuerySelector(".wgroup + .wgroup__foot")!.TextContent);
    }

    [Fact]
    public void An_ops_action_that_fails_tells_the_operator()
    {
        // No LineOpsDbContext is registered on this bench, so the write behind the confirm
        // throws when it asks for one — as a dropped connection would.
        var panel = RenderOps(LiveFeed());

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();
        panel.FindAll(".opsconfirm button").Single(b => b.TextContent.Trim() == "Scan automatically").Click();

        panel.WaitForAssertion(() =>
        {
            var said = Services.GetRequiredService<DeskToasts>().Last;
            Assert.NotNull(said);
            Assert.Equal(DeskState.Negative, said.State);
            Assert.Equal("Scanning was not changed. Try again.", said.Message);
        });

        // The window is still the window: its rows, its group, and no raw exception text.
        Assert.Contains("The Odds API", panel.Markup);
        Assert.NotNull(panel.Find("section.wsection[aria-label=Scanning] .wgroup__row--form"));
        Assert.DoesNotContain("InvalidOperationException", panel.Markup);
    }

    [Fact]
    public void Choosing_automatic_asks_first_and_cancel_withdraws_the_question()
    {
        var panel = RenderOps(LiveFeed());

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();

        var ask = panel.Find(".opsconfirm");
        Assert.Contains("Scan at this price until switched back", ask.TextContent);

        panel.FindAll(".opsconfirm button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        Assert.Empty(panel.FindAll(".opsconfirm"));
        Assert.Equal("true", panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "On request")
            .GetAttribute("aria-checked"));
    }

    [Fact]
    public void Ops_without_a_live_feed_keeps_the_prose_as_a_footnote()
    {
        var panel = RenderOps();

        Assert.Empty(panel.FindAll(".wgroup__label"));
        var footnote = panel.Find(".opsfoot");
        Assert.DoesNotContain("wgroup__foot", footnote.ClassName);
        Assert.Contains("Lines are pulled on request only", footnote.TextContent);
    }

    // ---- Incidents ---------------------------------------------------------------------------

    private static Incident Incident(int id, IncidentStatus status, string? rootCause = null) => new()
    {
        Id = id,
        Title = $"Incident {id}",
        Severity = AlertSeverity.Critical,
        Status = status,
        OpenedAt = new DateTimeOffset(2026, 10, 20, 14, 0, 0, TimeSpan.Zero),
        ResolvedAt = status == IncidentStatus.Resolved ? new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero) : null,
        RootCause = rootCause,
        CorrectiveActions = rootCause
    };

    private IRenderedComponent<IncidentsPanel> RenderIncidents(params Incident[] incidents)
    {
        var panel = RenderComponent<IncidentsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_incidents", incidents);
        Set(panel.Instance, "_openIncidents", incidents.Where(i => i.Status != IncidentStatus.Resolved).ToList());
        Set(panel.Instance, "_resolvedIncidents", incidents.Where(i => i.Status == IncidentStatus.Resolved).ToList());
        Loaded(panel.Instance);
        panel.Render();
        return panel;
    }

    [Fact]
    public void Open_and_resolved_incidents_are_two_named_lists()
    {
        var panel = RenderIncidents(
            Incident(1, IncidentStatus.Open),
            Incident(2, IncidentStatus.Resolved, "A cause."));

        var titles = panel.FindAll(".psection__head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(["Open", "Resolved"], titles);

        Assert.Contains("Incident 1", Section(panel, "Open"));
        Assert.DoesNotContain("Incident 2", Section(panel, "Open"));
        Assert.Contains("Incident 2", Section(panel, "Resolved"));
    }

    [Fact]
    public void With_nothing_open_the_list_is_only_the_record()
    {
        var panel = RenderIncidents(Incident(2, IncidentStatus.Resolved, "A cause."));

        var titles = panel.FindAll(".psection__head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(["Resolved"], titles);

        // The record is quiet: no state capsule saying Resolved, no red severity, a written RCA in words.
        var resolved = panel.FindAll("section.psection").Single();
        Assert.Empty(resolved.QuerySelectorAll(".tag"));
        Assert.Contains("Written", resolved.TextContent);
        Assert.Contains("1.5h", resolved.TextContent);
    }

    /// <summary>The incident list, then one incident opened onto its timeline and RCA.</summary>
    private IRenderedComponent<IncidentsPanel> RenderIncident(Incident incident)
    {
        var panel = RenderIncidents(incident);

        Set(panel.Instance, "_selected", incident);
        panel.Render();
        return panel;
    }

    /// <summary>
    /// The RCA is the grouped form every other form on the desk is: its two answers are rows of
    /// one group, each under its own name, and the rule they answer to is the group's footer. It
    /// was a .pform of two stacked text areas with their labels above them and a footnote of its
    /// own, a third look for a form.
    /// </summary>
    [Fact]
    public void The_rca_is_a_grouped_form()
    {
        var panel = RenderIncident(Incident(4, IncidentStatus.Open));

        var form = panel.Find(".pform--grouped");
        var rows = form.QuerySelectorAll("section.wsection .wgroup__row--form");

        Assert.Equal(2, rows.Length);
        Assert.Equal(["Root cause", "Corrective action"],
            rows.Select(r => r.QuerySelector(".wgroup__label")!.TextContent.Trim()).ToArray());
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.QuerySelector("textarea")!.GetAttribute("aria-label"))));

        Assert.Contains("Both are needed to resolve", form.QuerySelector("section.wsection .wgroup__foot")!.TextContent);
        Assert.Empty(form.QuerySelectorAll(".opsfoot"));

        var filled = Assert.Single(form.QuerySelectorAll(".desk-btn--filled"));
        Assert.Contains("Resolve incident", filled.TextContent);
    }

    /// <summary>
    /// A note the database would not take is said beside the note, and the words stay in the
    /// field to send again. The write used to have no catch at all, so a failure dropped the
    /// circuit and the typed note with it. No IncidentService is registered here, so the write
    /// throws the moment it asks for one.
    /// </summary>
    [Fact]
    public void A_note_that_fails_to_save_keeps_its_text()
    {
        var panel = RenderIncident(Incident(4, IncidentStatus.Open));

        panel.Find("input[aria-label='New timeline note']").Input("network blip");
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Add").Click();

        panel.WaitForAssertion(() =>
            Assert.Contains("The note was not saved. Try again.", panel.Find(".note").TextContent));
        Assert.Equal("network blip", panel.Find("input[aria-label='New timeline note']").GetAttribute("value"));
    }

    /// <summary>Mark mitigated says when it did not, under the form it sits in.</summary>
    [Fact]
    public void A_mitigation_that_fails_says_so()
    {
        var panel = RenderIncident(Incident(4, IncidentStatus.Open));

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Mark mitigated").Click();

        panel.WaitForAssertion(() =>
            Assert.Contains("The incident was not updated. Try again.", panel.Find(".pform--grouped .note").TextContent));
    }

    // ---- Runs --------------------------------------------------------------------------------

    private static IngestionRun Run(long id, RunStatus status) => new()
    {
        Id = id,
        SourceId = 1,
        Source = new Source { Id = 1, Key = "espn", Name = "ESPN (unofficial)" },
        JobKey = "stats:daily",
        StartedAt = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
        FinishedAt = new DateTimeOffset(2026, 10, 26, 12, 0, 2, TimeSpan.Zero),
        Status = status,
        RowsIngested = 1200,
        RequestsMade = 1
    };

    [Fact]
    public void Runs_say_success_quietly_and_tag_a_failure()
    {
        var panel = RenderComponent<RunsPanel>();

        Set(panel.Instance, "_runs", new List<IngestionRun> { Run(1, RunStatus.Success), Run(2, RunStatus.Failed) });
        Loaded(panel.Instance);
        panel.Render();

        var tags = panel.FindAll(".deskgrid .tag").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(["Failed"], tags);
        Assert.Contains(panel.FindAll(".deskgrid .quiet"), q => q.TextContent.Trim() == "Success");
    }

    [Fact]
    public void Runs_has_no_rule_repeating_its_title_and_explains_itself_in_a_footer()
    {
        var panel = RenderComponent<RunsPanel>();

        Set(panel.Instance, "_runs", new List<IngestionRun> { Run(1, RunStatus.Success) });
        Loaded(panel.Instance);
        panel.Render();

        Assert.Empty(panel.FindAll("section.psection"));
        Assert.Empty(panel.FindAll(".note"));
        Assert.Contains("Every run lands here", panel.Find(".deskgrid + .opsfoot").TextContent);
    }

    // ---- History -----------------------------------------------------------------------------

    private IRenderedComponent<HistoryPanel> RenderHistory()
    {
        var panel = RenderComponent<HistoryPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_eligibility", new List<SourceEligibility> { new("espn", true, "Unmetered and available.") });
        Set(panel.Instance, "_sourceNames",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["espn"] = "ESPN (unofficial)" });
        Set(panel.Instance, "_coverage", new BackfillCoverage(
            DaysRequested: 420, TargetDayFetches: 3200, Completed: 3054, Failed: 0, GamesFound: 211159,
            RowsIngested: 1234567, RequestsMade: 3054,
            EarliestCovered: new DateOnly(2025, 9, 4), LatestCovered: new DateOnly(2026, 10, 23)));
        Loaded(panel.Instance);
        panel.Render();
        return panel;
    }

    [Fact]
    public void History_figures_carry_thousands_separators_and_dates_are_written_out()
    {
        var panel = RenderHistory();

        var coverage = Section(panel, "Coverage");
        Assert.Contains("3,054", coverage);
        Assert.Contains("211,159", coverage);
        Assert.Contains("of 3,200 wanted", coverage);
        Assert.Contains("Sep 4, 2025", coverage);
        Assert.Contains("Oct 23, 2026", coverage);
        Assert.DoesNotContain("2025-09-04", coverage);
    }

    [Fact]
    public void History_names_its_sources_rather_than_showing_their_keys()
    {
        var panel = RenderHistory();

        var sources = panel.FindAll("section.psection").First(s => s.TextContent.Contains("Sources"));
        Assert.Equal("ESPN (unofficial)", sources.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.Empty(sources.QuerySelectorAll(".tag"));
    }

    [Fact]
    public void History_run_group_is_a_section()
    {
        var panel = RenderHistory();

        var run = panel.Find("section.wsection[aria-label=Run]");
        var labels = run.QuerySelectorAll(".wgroup__label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(["Past days", "Held days", "Sports", "Dates", "Range"], labels);
        Assert.Contains("re-resolved onto themselves", run.QuerySelector(".wgroup + .wgroup__foot")!.TextContent);

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk…").Click();

        var ask = panel.FindAll(".opsrow__text").Single(t => t.TextContent.Contains("again?")).TextContent;
        Assert.Equal("Walk 3,054 held days again?", string.Join(' ', ask.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Contains(panel.FindAll("button"), b => b.TextContent.Trim() == "Re-walk");
        Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Contains("Yes,"));
    }

    [Fact]
    public void A_re_walk_that_fails_says_so_in_the_run_group()
    {
        // ForgetAsync asks its scope for a LineOpsDbContext this bench does not register, so the
        // re-walk throws before anything is cleared — as a dropped connection would.
        var panel = RenderHistory();

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk…").Click();
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk").Click();

        var note = panel.WaitForElement("section.wsection[aria-label=Run] .note");
        Assert.Contains("The walk did not start. Try again.", note.TextContent);
        Assert.Contains("note--warn", note.ClassName);

        // The keys are back, and the record the window holds is still on it.
        Assert.Contains(panel.FindAll("button"), b => b.TextContent.Trim() == "Re-walk…");
        Assert.Contains("3,054", Section(panel, "Coverage"));
    }
}
