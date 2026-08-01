using LineOps.Reliability;

namespace LineOps.Tests.Reliability;

/// <summary>
/// The runbook is markdown on disk and elements on the incident panel. These pin the translation,
/// because the panel is read while an incident is being worked and a line that renders as raw
/// asterisks is a line the on-call has to mentally decode at the worst possible moment.
/// </summary>
public class RunbookParsingTests
{
    private static string Text(RunbookLine line)
        => string.Concat(line.Spans.Select(s => s.Text));

    [Fact]
    public void BoldBecomesABoldSpanWithoutItsMarkers()
    {
        var line = Runbook.ParseLine("**Means:** no successful ingestion run.");

        Assert.Collection(line.Spans,
            s =>
            {
                Assert.Equal(RunbookSpanKind.Bold, s.Kind);
                Assert.Equal("Means:", s.Text);
            },
            s =>
            {
                Assert.Equal(RunbookSpanKind.Text, s.Kind);
                Assert.Equal(" no successful ingestion run.", s.Text);
            });
    }

    [Fact]
    public void InlineCodeBecomesACodeSpan()
    {
        var line = Runbook.ParseLine("Configurable via `Reliability:FreshnessSlo`.");

        Assert.Contains(line.Spans,
            s => s.Kind == RunbookSpanKind.Code && s.Text == "Reliability:FreshnessSlo");

        // And no backticks survive into the rendered text.
        Assert.DoesNotContain('`', Text(line));
    }

    [Fact]
    public void ALinkKeepsItsTextAndDropsItsTarget()
    {
        var line = Runbook.ParseLine("See [ADR 0003](adr/0003-two-kinds-of-zero.md) for the reasoning.");

        // The target is a repository path. Rendering it would offer the reader a route to nowhere.
        Assert.Equal("See ADR 0003 for the reasoning.", Text(line));
        Assert.DoesNotContain("adr/", Text(line));
    }

    [Theory]
    [InlineData("1. Check the Ingestion Runs page.")]
    [InlineData("2. Failing → read the error on the most recent run.")]
    [InlineData("- A mix of Partial runs means empty payloads.")]
    public void NumberedAndBulletedLinesAreStepsWithTheMarkerRemoved(string source)
    {
        var line = Runbook.ParseLine(source);

        Assert.True(line.IsStep);
        Assert.DoesNotMatch(@"^(\d+\.|[-*])\s", Text(line));
    }

    [Fact]
    public void ProseIsNotAStep()
    {
        Assert.False(Runbook.ParseLine("**Urgency:** real. Every downstream number is stale.").IsStep);
    }

    [Fact]
    public void MixedEmphasisOnOneLineSplitsIntoOrderedSpans()
    {
        var line = Runbook.ParseLine("**Means:** a `429` from the provider.");

        Assert.Equal(
            [RunbookSpanKind.Bold, RunbookSpanKind.Text, RunbookSpanKind.Code, RunbookSpanKind.Text],
            line.Spans.Select(s => s.Kind));

        Assert.Equal("Means: a 429 from the provider.", Text(line));
    }

    [Fact]
    public void PlainTextSurvivesUnchanged()
    {
        const string plain = "Usually resolves itself; the circuit breaker will re-close.";

        var line = Runbook.ParseLine(plain);

        Assert.Equal(plain, Text(line));
        Assert.Single(line.Spans);
    }

    [Fact]
    public void EveryLineOfEverySectionRoundTripsWithoutLosingItsWords()
    {
        // Guards against a regex that silently eats content. The rendered text may drop markers
        // and link targets, but it must never come back empty for a line that had words in it.
        foreach (var entry in Runbook.All.Values)
        {
            foreach (var source in entry.Body)
            {
                var rendered = Text(Runbook.ParseLine(source));

                Assert.False(string.IsNullOrWhiteSpace(rendered),
                    $"[{entry.RuleKey}] rendered to nothing: {source}");
            }
        }
    }

    [Fact]
    public void NoRenderedLineStillCarriesMarkdownEmphasis()
    {
        foreach (var entry in Runbook.All.Values)
        {
            foreach (var rendered in entry.Body.Select(b => Text(Runbook.ParseLine(b))))
            {
                Assert.DoesNotContain("**", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain('`', rendered);
            }
        }
    }
}
