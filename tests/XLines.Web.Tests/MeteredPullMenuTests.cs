using Bunit;
using XLines.Desk;
using XLines.Desk.Primitives;
using XLines.Ingestion.Services;
using XLines.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Pull data menu as a desk with no Odds API key draws it (ESPN on or off): every metered entry
/// sits under "Costs credits", dimmed, saying "needs a key" where its price would be — never "free" —
/// with the key's place as its reason and tooltip. Free pulls beside them still say "free".
/// </summary>
public class MeteredPullMenuTests : DeskTestContext
{
    private sealed class Menu(IReadOnlyList<IngestionJob> jobs) : IDeskJobs
    {
        public Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeskJob>>(jobs.Select(IngestionDeskJobs.Describe).ToList());

        public Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
            => throw new InvalidOperationException("Nothing is run in this test.");
    }

    public MeteredPullMenuTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddScoped<DeskJobRunner>();
    }

    private static IngestionJob Metered(string key, string label, int credits)
        => new(key, label, "What it pulls.", 0, Available: false, Unavailable: IngestionJobs.NoOddsKey,
            EstimatedCredits: credits, Group: JobGroups.Lines,
            Hint: "Pulls from The Odds API. " + IngestionJobs.NoOddsKey, Metered: true, NeedsKey: true);

    [Fact]
    public void With_no_key_no_metered_entry_reads_free_and_each_names_the_key_as_the_remedy()
    {
        var jobs = new Menu(
        [
            new("settle", "Settle finished games", "Grades entries.", 0, true, null, Group: JobGroups.Housekeeping),
            Metered("odds:lines:mlb", "Pull lines — MLB", 0),
            Metered("odds:lines:nfl", "Pull lines — NFL", 0),
            Metered("odds:scores:nba", "Pull scores — NBA", 2)
        ]);

        var cut = RenderComponent<DeskJobMenu>(p => p.Add(x => x.Label, "Pull data").Add(x => x.Jobs, jobs));
        cut.Find("button.desk-btn").Click();

        var metered = cut.FindAll("[role=menuitem]").Where(r => r.QuerySelector(".menu__label")!.TextContent.StartsWith("Pull ")).ToList();
        Assert.Equal(3, metered.Count);

        foreach (var row in metered)
        {
            var detail = row.QuerySelector(".menu__detail")!.TextContent;
            Assert.Equal("needs a key", detail);
            Assert.DoesNotContain("free", row.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("true", row.GetAttribute("aria-disabled"));
            Assert.Contains("Ops → Odds feeds", row.QuerySelector(".menu__desc")!.TextContent);
            Assert.Contains("Ops → Odds feeds", row.GetAttribute("title"));
            Assert.DoesNotContain("registered", row.TextContent);
        }

        var settle = cut.FindAll("[role=menuitem]").Single(r => r.QuerySelector(".menu__label")!.TextContent == "Settle finished games");
        Assert.Equal("free", settle.QuerySelector(".menu__detail")!.TextContent);
    }
}
