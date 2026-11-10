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
/// The Runs window's Detail column: the sentence the run wrote about itself, then its error when
/// it has one. Three closing-line runs that each said Success with nothing beside them were what
/// made "523 requests in 340 ms" impossible to read (plan 2026-11-09, finding 4).
/// </summary>
public class RunsDetailTests : DeskTestContext
{
    public RunsDetailTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(new UnreachableFactory());
        Services.AddSingleton<HistoryBackfillService>();
        Services.AddSingleton<BackfillCoordinator>();
    }

    private sealed class UnreachableFactory : IDbContextFactory<LineOpsDbContext>
    {
        public LineOpsDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<LineOpsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new TaskCompletionSource<LineOpsDbContext>().Task;
    }

    private static IngestionRun Run(long id, RunStatus status, string? detail, string? error) => new()
    {
        Id = id,
        SourceId = 1,
        Source = new Source { Id = 1, Key = "espn", Name = "ESPN (unofficial)" },
        JobKey = "espn:closing-lines",
        StartedAt = new DateTimeOffset(2026, 11, 9, 23, 0, 0, TimeSpan.Zero),
        FinishedAt = new DateTimeOffset(2026, 11, 9, 23, 0, 1, TimeSpan.Zero),
        Status = status,
        Detail = detail,
        Error = error
    };

    private IRenderedComponent<RunsPanel> Render(params IngestionRun[] runs)
    {
        var panel = RenderComponent<RunsPanel>();

        Set(panel.Instance, "_runs", runs.ToList());
        typeof(LineOps.Desk.Windowing.DeskPanelBase).GetProperty("LoadError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel.Instance, null);
        typeof(LineOps.Desk.Windowing.DeskPanelBase).GetProperty("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel.Instance, true);
        panel.Render();
        return panel;
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

    [Fact]
    public void A_run_says_what_it_did()
    {
        var panel = Render(Run(1, RunStatus.Success, "NFL: 2 asked, 0 answered; 2 set aside until Nov 23.", null));

        var detail = panel.FindAll(".deskgrid .trunc").Single();
        Assert.Equal("NFL: 2 asked, 0 answered; 2 set aside until Nov 23.", detail.TextContent.Trim());
        Assert.Contains("quiet", detail.ClassList);
    }

    [Fact]
    public void A_failed_run_says_what_it_did_and_then_its_error_in_the_negative_ink()
    {
        var panel = Render(Run(1, RunStatus.Failed, "UFC: 3 asked, 0 answered.", "DbUpdateException: the write failed."));

        var cells = panel.FindAll(".deskgrid .trunc");
        Assert.Equal(["UFC: 3 asked, 0 answered.", "DbUpdateException: the write failed."],
            cells.Select(c => c.TextContent.Trim()));
        Assert.Contains("down", cells[1].ClassList);
    }

    [Fact]
    public void A_partial_run_keeps_its_error_quiet_and_a_run_without_a_detail_still_shows_its_error()
    {
        var panel = Render(Run(1, RunStatus.Partial, null, "2 of 8 games without plays (0 failed)."));

        var error = panel.FindAll(".deskgrid .trunc").Single();
        Assert.Equal("2 of 8 games without plays (0 failed).", error.TextContent.Trim());
        Assert.Contains("quiet", error.ClassList);
    }
}
