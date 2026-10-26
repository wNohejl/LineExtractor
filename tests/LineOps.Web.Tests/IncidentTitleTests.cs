using System.Reflection;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// An incident's title names its severity once. The meta line under the name read
/// "#4 · Critical · opened …" while a Critical tag sat in the toolbar below it, so the one
/// fact the operator triages by was said twice in two registers. The severity and status tags
/// now sit on the title's baseline, the meta line carries the number and when it opened, and
/// the toolbar holds only its keys.
///
/// The incident is placed on the panel directly: its service reads Postgres, and what is under
/// test is how the selected incident is drawn, not how it was fetched.
/// </summary>
public class IncidentTitleTests : DeskTestContext
{
    public IncidentTitleTests() => Services.AddScoped<DeskToasts>();

    private IRenderedComponent<IncidentsPanel> RenderSelected(Incident incident)
    {
        var panel = RenderComponent<IncidentsPanel>();

        typeof(IncidentsPanel)
            .GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel.Instance, incident);

        panel.Render();
        return panel;
    }

    private static Incident Feed() => new()
    {
        Id = 4,
        Title = "Odds feed stalled",
        Severity = AlertSeverity.Critical,
        Status = IncidentStatus.Open,
        OpenedAt = new DateTimeOffset(2026, 10, 20, 14, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Severity_and_status_ride_the_title()
    {
        var panel = RenderSelected(Feed());

        var tags = panel.FindAll(".ptitle__row .tag").Select(t => t.TextContent.Trim()).ToList();

        Assert.Equal(["Critical", "Open"], tags);
    }

    [Fact]
    public void Severity_is_said_once()
    {
        var panel = RenderSelected(Feed());

        var meta = panel.Find(".ptitle__text").TextContent;
        Assert.Contains("#4", meta);
        Assert.Contains("opened", meta);
        Assert.DoesNotContain("Critical", meta);

        Assert.Single(panel.FindAll(".tag"), t => t.TextContent.Trim() == "Critical");
    }

    [Fact]
    public void The_toolbar_keeps_only_its_keys()
    {
        var panel = RenderSelected(Feed());

        var toolbar = panel.Find(".ptoolbar");
        Assert.Empty(toolbar.QuerySelectorAll(".tag"));
        Assert.Contains("All incidents", toolbar.TextContent);
    }
}
