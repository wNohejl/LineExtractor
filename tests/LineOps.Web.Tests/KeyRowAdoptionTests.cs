using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The operations windows lay their keys through the Desk's KeyRow and ask their questions
/// through InlineConfirm. A disabled key's title never shows — a disabled MudButton takes no
/// pointer events — so a key that waits says why in text on its row, and every confirm reads
/// Cancel first. Read from the source: most of these windows need a database to render.
/// </summary>
public class KeyRowAdoptionTests
{
    private static readonly string[] Panels =
        ["OpsPanel", "HistoryPanel", "IncidentsPanel", "PerformancePanel", "PlayersPanel"];

    private static string Source(string panel) => File.ReadAllText(Path.Combine(
        Repo.Root(), "src", "LineOps.Web", "Components", "Panels", panel + ".razor"));

    /// <summary>Each DeskButton tag through its close, label included.</summary>
    private static IEnumerable<string> Buttons(string source) =>
        Regex.Matches(source, @"<DeskButton\b.*?(?:</DeskButton>|/>)", RegexOptions.Singleline)
            .Select(m => m.Value);

    [Theory]
    [MemberData(nameof(PanelNames))]
    public void No_disabled_key_explains_itself_only_in_a_title(string panel)
    {
        var offenders = Buttons(Source(panel))
            .Where(b => b.Contains("Disabled=") && b.Contains("Title="))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_inline_confirm_is_the_primitive()
    {
        foreach (var panel in Panels)
        {
            var source = Source(panel);
            Assert.DoesNotContain("opsconfirm", source);
        }

        Assert.Matches(@"@if \(_confirmRewalk\)\s*\{\s*<InlineConfirm\b", Source("HistoryPanel"));
        Assert.Matches(@"@if \(_confirmAutomatic\)\s*\{\s*<InlineConfirm\b", Source("OpsPanel"));
    }

    [Theory]
    [InlineData("OpsPanel")]
    [InlineData("HistoryPanel")]
    [InlineData("IncidentsPanel")]
    [InlineData("PerformancePanel")]
    public void Keys_that_wait_sit_in_a_key_row(string panel)
    {
        var source = Source(panel);

        Assert.Contains("<KeyRow", source);
        Assert.DoesNotContain("opsact__why", source);
    }

    /// <summary>A sentence that is the whole of a state is the Desk's EmptyState or Note.</summary>
    [Fact]
    public void Lone_sentences_are_the_desks_own()
    {
        foreach (var panel in Panels)
        {
            var source = Source(panel);
            Assert.DoesNotContain("class=\"opsnone\"", source);
            Assert.DoesNotContain("<p class=\"empty\"", source);
        }

        Assert.Contains("<EmptyState Kind=\"EmptyStateKind.New\">Nothing on the timeline yet.</EmptyState>",
            Source("IncidentsPanel"));
    }

    [Fact]
    public void The_drill_key_and_the_postponed_count_say_what_they_mean()
    {
        Assert.Contains("Run the drill", Source("OpsPanel"));
        Assert.DoesNotContain("Run ingest", Source("OpsPanel"));

        var history = Source("HistoryPanel");
        Assert.Contains(" postponed</span>", history);
        Assert.DoesNotContain(" pp</span>", history);
    }

    public static TheoryData<string> PanelNames()
    {
        var data = new TheoryData<string>();
        foreach (var panel in Panels)
            data.Add(panel);
        return data;
    }
}
