using System.Reflection;
using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Desk.Primitives;
using XLines.Ingestion.Services;
using XLines.Reliability;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

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
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new HangingDbFactory());
        Services.AddSingleton<HistoryBackfillService>();
        Services.AddSingleton<BackfillCoordinator>();
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
    private static void Loaded(object panel) => PanelProbe.MarkLoaded(panel);

    /// <summary>
    /// A section card (the Desk's grouping contract, Nov 22) by the title in its bar: the section,
    /// whose card is its first child and whose footers follow it.
    /// </summary>
    private static AngleSharp.Dom.IElement Card(IRenderedFragment panel, string title)
        => panel.FindAll("section.wsection")
            .Single(s => s.QuerySelector(":scope > .wgroup > .wgroup__bar > .wgroup__title")?.TextContent.Trim() == title);

    private static string Section(IRenderedFragment panel, string title) => Card(panel, title).OuterHtml;

    /// <summary>The cards' titles in a window's flows, in order.</summary>
    private static IReadOnlyList<string> CardTitles(IRenderedFragment panel)
        => panel.FindAll(".wflow > section.wsection > .wgroup > .wgroup__bar > .wgroup__title")
            .Select(h => h.TextContent.Trim()).ToList();

    private static string Words(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

        // The card is named Scanning in its bar; its one row is the mode.
        var section = Card(panel, "Scanning");
        var row = section.QuerySelector(".wgroup .wgroup__row--form")!;
        Assert.Equal("Mode", row.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.NotNull(row.QuerySelector("[role=radiogroup]"));

        // The footer is the section's, directly under the group it prices.
        var footer = section.QuerySelector(".wgroup + .wgroup__foot")!;
        Assert.Contains("Lines are pulled on request only", footer.TextContent);
        Assert.Contains("Set by configuration until it is chosen here.", footer.TextContent);

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();

        var keys = panel.FindAll(".confirm button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Cancel", "Scan automatically"], keys);
    }

    [Fact]
    public void Ops_drill_faults_are_a_section_with_what_the_drill_does_as_footer()
    {
        var panel = RenderComponent<OpsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        Set(panel.Instance, "_injectable", new List<Source> { new() { Id = 1, Key = "theoddsapi", Name = "The Odds API" } });
        Loaded(panel.Instance);
        panel.Render();

        var faults = Card(panel, "On-call drill");
        Assert.Equal("The Odds API", faults.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.Contains("Inject a fault, run an ingest", faults.QuerySelector(".wgroup + .wgroup__foot")!.TextContent);
    }

    [Fact]
    public void An_ops_action_that_fails_tells_the_operator()
    {
        // No XLinesDbContext is registered on this bench, so the write behind the confirm
        // throws when it asks for one — as a dropped connection would.
        var panel = RenderOps(LiveFeed());

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();
        panel.FindAll(".confirm button").Single(b => b.TextContent.Trim() == "Scan automatically").Click();

        panel.WaitForAssertion(() =>
        {
            var said = Services.GetRequiredService<DeskToasts>().Last;
            Assert.NotNull(said);
            Assert.Equal(DeskState.Negative, said.State);
            Assert.Equal("Scanning was not changed. Try again.", said.Message);
        });

        // The window is still the window: its rows, its group, and no raw exception text.
        Assert.Contains("The Odds API", panel.Markup);
        Assert.NotNull(Card(panel, "Scanning").QuerySelector(".wgroup__row--form"));
        Assert.DoesNotContain("InvalidOperationException", panel.Markup);
    }

    [Fact]
    public void Choosing_automatic_asks_first_and_cancel_withdraws_the_question()
    {
        var panel = RenderOps(LiveFeed());

        panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "Automatic").Click();

        var ask = panel.Find(".confirm");
        Assert.Contains("Scan at this price until switched back", ask.TextContent);

        panel.FindAll(".confirm button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        Assert.Empty(panel.FindAll(".confirm"));
        Assert.Equal("true", panel.FindAll("[role=radio]").Single(b => b.TextContent.Trim() == "On request")
            .GetAttribute("aria-checked"));
    }

    /// <summary>With no switch to price, the prose that prices scanning is the Odds feeds card's footer.</summary>
    [Fact]
    public void Ops_without_a_live_feed_keeps_the_prose_as_the_feeds_footer()
    {
        var panel = RenderOps();

        Assert.Empty(panel.FindAll(".wgroup__label"));
        Assert.DoesNotContain("Scanning", CardTitles(panel));
        var footer = Card(panel, "Odds feeds").QuerySelector(":scope > .wgroup + .wgroup__foot")!;
        Assert.Contains("Lines are pulled on request only", footer.TextContent);
    }

    /// <summary>
    /// The Ops window is a flow of section cards: each section a card named in its bar, the tables
    /// spanning the row so a column has the window's width, the short cards a track each. It was one
    /// long column of bare heading rules, the sections running together.
    /// </summary>
    [Fact]
    public void Ops_sections_are_cards_in_one_flow()
    {
        var panel = RenderOps(LiveFeed());

        Assert.Equal(["Stats source", "Open alerts", "Source health", "Odds feeds", "Scanning", "On-call drill"],
            CardTitles(panel));
        Assert.Single(panel.FindAll(".wflow.opsflow"));
        Assert.Empty(panel.FindAll("section.psection, .wgroup__head"));

        // A table spans the row; the all-clear sentence and the forms take one track.
        Assert.Contains("wsection--span", Card(panel, "Source health").ClassName);
        Assert.Contains("wsection--span", Card(panel, "Odds feeds").ClassName);
        Assert.DoesNotContain("wsection--span", Card(panel, "Open alerts").ClassName);
        Assert.DoesNotContain("wsection--span", Card(panel, "Scanning").ClassName);

        // One edge a section: no card inside a card, and the tiles stay a row of tiles outside them.
        Assert.Empty(panel.FindAll(".wgroup .wgroup"));
        Assert.Empty(panel.FindAll(".wgroup .metrics"));
        Assert.NotNull(panel.Find(".metrics"));

        // Every card is labelled by its bar's title.
        foreach (var card in panel.FindAll(".wflow > section.wsection"))
        {
            var id = card.GetAttribute("aria-labelledby");
            Assert.False(string.IsNullOrEmpty(id));
            Assert.Equal(id, card.QuerySelector(".wgroup__title")!.Id);
        }
    }

    /// <summary>
    /// The feeds' Books keep a reading width: each book one name on one line, the list a block with
    /// a floor (scoped CSS), so it wraps between books rather than stacking one word a line.
    /// </summary>
    [Fact]
    public void Odds_feed_books_are_names_that_wrap_between_books()
    {
        var panel = RenderOps(LiveFeed());

        var books = Card(panel, "Odds feeds").QuerySelector(".opsbooks")!;
        var names = books.QuerySelectorAll(".opsbooks__name").Select(n => n.TextContent.Trim()).ToList();
        Assert.Equal(2, names.Count);
        Assert.EndsWith(",", names[0]);
        Assert.DoesNotContain(",", names[1]);
        Assert.Equal("1 of 1 live", Words(Card(panel, "Odds feeds").QuerySelector(".wgroup__keys")!.TextContent));
    }

    // ---- The stats source, three states (ADR 0026) ------------------------------------------

    private static string StatsSection(IRenderedFragment panel) => Words(Card(panel, "Stats source").TextContent);

    private static IReadOnlyList<string> Tooltips(IRenderedFragment panel)
        => panel.FindComponents<DeskTooltip>().Select(t => t.Instance.Text).ToList();

    /// <summary>
    /// ESPN off and no key — a fresh clone: Ops says first that nothing supplies fixtures or scores
    /// and that the fix is the key field below, then why ESPN is opt-in and exactly how to turn it on.
    /// </summary>
    [Fact]
    public void Ops_with_espn_off_and_no_key_asks_for_a_key_and_says_how_espn_is_turned_on()
    {
        var panel = RenderOps();

        Assert.Equal("Stats source", CardTitles(panel)[0]);

        var section = Card(panel, "Stats source");
        var text = StatsSection(panel);
        Assert.Equal("None", section.QuerySelector(".wgroup__keys")!.TextContent.Trim());

        Assert.Contains("No stats source: ESPN is off and no Odds API key is held", section.QuerySelector(".empty")!.TextContent);
        Assert.Equal("0", section.QuerySelector(".empty")!.GetAttribute("tabindex"));
        Assert.Contains("Enter your free key under Odds feeds below", text);
        Assert.Contains("unofficial and undocumented", text);
        Assert.Contains("personal, non-commercial use and prohibit automated access", text);
        Assert.Contains("never to host, redistribute or sell it", text);
        Assert.Contains("ESPN_ENABLED=true in .env", text);
        Assert.Contains("start the stack again", text);
        Assert.Contains("appsettings.Local.json", text);

        // An empty state in the card and the notice under it: one edge, no grid, no second edge
        // around the notice.
        Assert.Single(section.QuerySelectorAll(".wgroup"));
        Assert.Empty(section.QuerySelectorAll(".deskgrid, .note"));
        Assert.Contains("unofficial and undocumented", section.QuerySelector(":scope > .wgroup + .wgroup__foot")!.TextContent);
    }

    [Fact]
    public void Ops_with_a_key_and_espn_off_says_the_odds_api_supplies_fixtures_and_scores_and_what_needs_espn()
    {
        var panel = RenderOps(LiveFeed());
        Set(panel.Instance, "_port", StatsPort.TheOddsApi);
        panel.Render();

        var text = StatsSection(panel);
        Assert.Equal("The Odds API", Card(panel, "Stats source").QuerySelector(".wgroup__keys")!.TextContent.Trim());
        Assert.Contains("The Odds API is the stats source on this host, on your key.", text);
        Assert.Contains("Fixtures are free", text);
        Assert.Contains("Final scores cost 2 credits a league a pull", text);
        Assert.Contains("Pull data → Pull scores", text);
        Assert.Contains("Box scores, player stats, play-by-play, injuries and UFC need ESPN, which is off.", text);
        Assert.Contains("ESPN_ENABLED=true in .env", text);

        // Not an empty state: the desk has a source, it is said in prose.
        Assert.DoesNotContain("No stats source", text);
    }

    [Fact]
    public void Ops_with_espn_on_says_espn_is_the_stats_source_and_has_no_notice()
    {
        Services.Configure<XLines.Ingestion.Configuration.IngestionOptions>(o => o.Espn.Enabled = true);

        var panel = RenderOps();

        Assert.Contains("ESPN is the stats source on this host", StatsSection(panel));
        Assert.DoesNotContain("ESPN_ENABLED", panel.Markup);
        Assert.DoesNotContain("No stats source", panel.Markup);
    }

    [Fact]
    public void The_stats_source_statement_carries_a_tooltip_saying_what_a_stats_source_is()
    {
        var panel = RenderOps();

        Assert.Contains(Tooltips(panel), t => t.StartsWith("The stats source writes each game's schedule and result"));
    }

    // ---- The month in one place (owner's request, 2026-11-20) ---------------------------------

    [Fact]
    public void Ops_states_credits_left_by_whose_count_when_they_reset_and_what_scores_would_take()
    {
        var panel = RenderOps(LiveFeed());
        Set(panel.Instance, "_allowance", new AllowanceState(500, 88, true, 100, new DateOnly(2026, 12, 1)));
        Set(panel.Instance, "_scores", new ScoresPlan(["nba", "nfl"], 2, 4, 11, Automatic: false));
        panel.Render();

        var section = Card(panel, "Scanning");
        var text = Words(section.TextContent);

        Assert.Contains("412 of 500 credits left this month, by The Odds API's own count; they reset on Dec 1.", text);
        Assert.Contains("Scores cost 2 credits a league a pull; on Automatic they would take about 4 a day (NBA, NFL).", text);

        // The figure explains itself on hover and focus; the figure itself is text.
        Assert.Contains(Tooltips(panel), t => t.StartsWith("The Odds API reports what is left on the key"));
        Assert.NotEmpty(section.QuerySelectorAll("span[tabindex='0'] .num"));
    }

    [Fact]
    public void On_automatic_the_scores_spend_to_the_months_end_is_stated_as_set_aside()
    {
        var panel = RenderOps(LiveFeed());
        Set(panel.Instance, "_allowance", new AllowanceState(500, 300, false, 100, new DateOnly(2026, 12, 1)));
        Set(panel.Instance, "_scores", new ScoresPlan(["nfl"], 2, 2, 11, Automatic: true));
        panel.Render();

        var text = Card(panel, "Scanning").TextContent;

        Assert.Contains("by this desk's count", text);
        Assert.Contains("about 2 a day, 22 to the month's end, set aside before lines are paced", text);
    }

    [Fact]
    public void The_scanning_switch_carries_a_tooltip_and_its_segments_say_what_each_spends()
    {
        var panel = RenderOps(LiveFeed());

        Assert.Contains(Tooltips(panel), t => t.StartsWith("On request spends nothing unless you choose a pull"));

        var segments = panel.FindAll("[role=radio]").Select(b => b.GetAttribute("title")).ToList();
        Assert.Contains(segments, t => t!.StartsWith("Nothing is spent unasked"));
        Assert.Contains(segments, t => t!.Contains("scores") && t.Contains("once a night"));
    }

    [Fact]
    public void The_odds_api_key_row_carries_a_tooltip_with_what_each_pull_costs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "xlines-opskey-" + Guid.NewGuid().ToString("N"));
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [XLines.Web.Services.DeskOddsKey.StorePathKey] = directory })
            .Build();
        var key = new XLines.Web.Services.DeskOddsKey(
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), new RuntimeOddsKey(),
            _ => Task.CompletedTask, new NoClients(), configuration,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<XLines.Web.Services.DeskOddsKey>.Instance);

        var panel = RenderOps(LiveFeed());
        Set(panel.Instance, "_oddsKey", key);
        panel.Render();

        // The card is named for the key; its one row is the key's value, its field named in full.
        var card = Card(panel, "The Odds API key");
        Assert.Equal("Key", card.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.NotNull(card.QuerySelector("input[type=password][aria-label='The Odds API key']"));
        Assert.Contains("the-odds-api.com", card.QuerySelector(":scope > .wgroup + .wgroup__foot")!.TextContent);
        Assert.Contains(Tooltips(panel), t => t.Contains("500 credits a month") && t.Contains("scores two a league") && t.Contains("fixtures nothing"));
    }

    private sealed class NoClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("No call is made in this test.");
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

        Assert.Equal(["Open", "Resolved"], CardTitles(panel));

        // Two tables, each a card across the row of one flow, and the tiles outside them.
        Assert.Single(panel.FindAll(".wflow.incflow"));
        Assert.All(panel.FindAll(".wflow > section.wsection"), s => Assert.Contains("wsection--span", s.ClassName));
        Assert.Empty(panel.FindAll("section.psection, .wgroup .metrics"));

        Assert.Contains("Incident 1", Section(panel, "Open"));
        Assert.DoesNotContain("Incident 2", Section(panel, "Open"));
        Assert.Contains("Incident 2", Section(panel, "Resolved"));
    }

    [Fact]
    public void With_nothing_open_the_list_is_only_the_record()
    {
        var panel = RenderIncidents(Incident(2, IncidentStatus.Resolved, "A cause."));

        Assert.Equal(["Resolved"], CardTitles(panel));

        // The record is quiet: no state capsule saying Resolved, no red severity, a written RCA in words.
        var resolved = Card(panel, "Resolved");
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
    /// An incident's timeline and its RCA are two cards of one flow — side by side in a wide
    /// window — each named in its bar, rather than two bare rules down one column.
    /// </summary>
    [Fact]
    public void The_timeline_and_the_rca_are_cards_of_one_flow()
    {
        var panel = RenderIncident(Incident(4, IncidentStatus.Open));

        var flow = panel.Find(".wflow.incflow");
        var titles = flow.QuerySelectorAll("section.wsection > .wgroup > .wgroup__bar > .wgroup__title")
            .Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(["Timeline", "Root-cause analysis"], titles);
        Assert.Empty(panel.FindAll("section.psection, .wgroup .wgroup"));

        // The composer is a row of the timeline card.
        Assert.NotNull(Card(panel, "Timeline").QuerySelector(".wgroup input[aria-label='New timeline note']"));
    }

    /// <summary>A note that would not save is said under the timeline card, never inside it.</summary>
    [Fact]
    public void A_note_that_fails_is_said_under_the_timeline_card()
    {
        var panel = RenderIncident(Incident(4, IncidentStatus.Open));

        panel.Find("input[aria-label='New timeline note']").Input("network blip");
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Add").Click();

        panel.WaitForAssertion(() =>
        {
            var timeline = Card(panel, "Timeline");
            Assert.NotNull(timeline.QuerySelector(":scope > .wgroup + .note"));
            Assert.Empty(timeline.QuerySelectorAll(".wgroup .note"));
        });
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
        // Success is a check mark whose word is kept for a screen reader, not a column of grey words.
        Assert.Contains(panel.FindAll(".deskgrid .ops-ok .sr-only"), q => q.TextContent.Trim() == "Success");
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

        // The tiles and their footnote stand on the window's body, outside every card.
        var coverage = panel.Find(".metrics").OuterHtml + panel.Find(".metrics + .opsfoot").OuterHtml;
        Assert.Empty(panel.FindAll(".wgroup .metrics"));
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

        var sources = Card(panel, "Sources");
        Assert.Equal("ESPN (unofficial)", sources.QuerySelector(".wgroup__label")!.TextContent.Trim());
        Assert.Empty(sources.QuerySelectorAll(".tag"));
    }

    [Fact]
    public void History_run_group_is_a_section()
    {
        var panel = RenderHistory();

        var run = Card(panel, "Run");
        var labels = run.QuerySelectorAll(".wgroup__label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(["Past days", "Held days", "Sports", "Dates", "Range"], labels);
        Assert.Contains("re-resolved onto themselves", run.QuerySelector(".wgroup + .wgroup__foot")!.TextContent);

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk…").Click();

        var ask = panel.Find(".confirm .confirm__ask").TextContent;
        Assert.Equal(["Cancel", "Re-walk"], panel.FindAll(".confirm button").Select(b => b.TextContent.Trim()).ToList());
        Assert.Equal("Walk 3,054 held days again?", string.Join(' ', ask.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Contains(panel.FindAll("button"), b => b.TextContent.Trim() == "Re-walk");
        Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Contains("Yes,"));
    }

    [Fact]
    public void A_re_walk_that_fails_says_so_in_the_run_group()
    {
        // ForgetAsync asks its scope for a XLinesDbContext this bench does not register, so the
        // re-walk throws before anything is cleared — as a dropped connection would.
        var panel = RenderHistory();

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk…").Click();
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Re-walk").Click();

        panel.WaitForAssertion(() => Assert.NotNull(Card(panel, "Run").QuerySelector(".note")));
        var note = Card(panel, "Run").QuerySelector(".note")!;
        Assert.Contains("The walk did not start. Try again.", note.TextContent);
        Assert.Contains("note--warn", note.ClassName);
        // Under the card, never inside it.
        Assert.Empty(Card(panel, "Run").QuerySelectorAll(".wgroup .note"));

        // The keys are back, and the record the window holds is still on it.
        Assert.Contains(panel.FindAll("button"), b => b.TextContent.Trim() == "Re-walk…");
        Assert.Contains("3,054", panel.Find(".metrics").TextContent);
    }

    /// <summary>
    /// History is the tiles, then one flow of cards: the sources and the walk's form a track each,
    /// the two record tables each across the row. It was four bare rules down one column.
    /// </summary>
    [Fact]
    public void History_sections_are_cards_in_one_flow()
    {
        var panel = RenderHistory();

        Assert.Equal(["Sources", "Run", "Seasons", "Recent days"], CardTitles(panel));
        Assert.Single(panel.FindAll(".wflow.histflow"));
        Assert.Empty(panel.FindAll("section.psection, .wgroup__head, .wgroup .wgroup"));

        Assert.DoesNotContain("wsection--span", Card(panel, "Sources").ClassName);
        Assert.DoesNotContain("wsection--span", Card(panel, "Run").ClassName);
        Assert.Contains("wsection--span", Card(panel, "Seasons").ClassName);
        Assert.Contains("wsection--span", Card(panel, "Recent days").ClassName);
    }

    /// <summary>
    /// The coordinator says it changed on every day it walks — a burst, during a backfill — and
    /// History reloads once per burst, through the second's settle every watching window keeps.
    /// It reloaded on each: five notices, five reads of the record.
    /// </summary>
    [Fact]
    public async Task History_reloads_once_for_a_burst_of_coordinator_changes()
    {
        // Each load's first read asks its scope for the database; counting those counts loads.
        var loads = 0;
        Services.AddScoped<XLinesDbContext>(_ =>
        {
            Interlocked.Increment(ref loads);
            throw new InvalidOperationException("No database in a render test.");
        });

        var panel = RenderComponent<HistoryPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));
        Assert.Equal(1, Volatile.Read(ref loads));

        var coordinator = Services.GetRequiredService<BackfillCoordinator>();
        var changed = (Action?)typeof(BackfillCoordinator)
            .GetField(nameof(BackfillCoordinator.Changed), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator);

        for (var i = 0; i < 5; i++)
            changed!.Invoke();

        panel.WaitForAssertion(() => Assert.Equal(2, Volatile.Read(ref loads)), TimeSpan.FromSeconds(5));

        // And no straggler after the settle: the burst was one reload, not one now and more later.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(2, Volatile.Read(ref loads));
    }
}
