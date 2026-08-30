using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LineOps.Reliability;

/// <summary>One rule's section of the runbook: what the alert means and what to do about it.</summary>
public record RunbookEntry(string RuleKey, string Heading, IReadOnlyList<string> Body);

public enum RunbookSpanKind
{
    Text,
    Bold,
    Code
}

/// <summary>A run of runbook text and how it was emphasised.</summary>
public record RunbookSpan(RunbookSpanKind Kind, string Text);

/// <summary>
/// One line of a runbook section, split into spans.
///
/// Spans rather than HTML: this library has no business deciding what emphasis looks like, and a
/// method returning markup would have to be trusted by whatever rendered it. Handing back what
/// each run *is* leaves the choice of element to the UI and leaves the escaping to the renderer
/// that already does it correctly.
/// </summary>
public record RunbookLine(bool IsStep, IReadOnlyList<RunbookSpan> Spans);

/// <summary>
/// The operator's runbook, read from the copy embedded in this assembly.
///
/// It lives here rather than as a file on disk because the incident panel needs the triage steps
/// while an incident is being worked, and a container that publishes the app but not the docs
/// folder would leave the on-call reading an empty panel. Embedding also keeps a single copy:
/// the same text the repository shows is the text the UI renders and the text the coverage test
/// asserts against.
/// </summary>
public static partial class Runbook
{
    /// <summary>Rule headings look like "## `freshness` — Critical". The backticked key is the contract.</summary>
    [GeneratedRegex(@"^##\s+`(?<key>[a-z_]+)`.*$", RegexOptions.Multiline)]
    private static partial Regex RuleHeading { get; }

    private static readonly Lazy<FrozenDictionary<string, RunbookEntry>> Entries = new(Parse);

    /// <summary>Every rule the runbook documents, keyed by rule key.</summary>
    public static FrozenDictionary<string, RunbookEntry> All => Entries.Value;

    public static RunbookEntry? Find(string ruleKey)
        => All.TryGetValue(ruleKey, out var entry) ? entry : null;

    /// <summary>A numbered or bulleted triage step, which reads as a step rather than as prose.</summary>
    [GeneratedRegex(@"^(\d+\.|[-*])\s+")]
    private static partial Regex StepMarker { get; }

    /// <summary>
    /// Inline markdown, in one pass so the alternatives cannot overlap: bold, code, or a link.
    /// A link becomes its text — the target is a repository path that means nothing to a reader
    /// looking at the panel.
    /// </summary>
    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|`(?<code>[^`]+)`|\[(?<link>[^\]]+)\]\([^)]*\)")]
    private static partial Regex Inline { get; }

    /// <summary>
    /// Splits one runbook line into spans, dropping the list marker if it has one.
    ///
    /// The runbook uses a deliberately small slice of markdown, so this handles that slice and
    /// treats everything else as literal text rather than pulling in a parser.
    /// </summary>
    public static RunbookLine ParseLine(string line)
    {
        var isStep = StepMarker.IsMatch(line);
        var text = StepMarker.Replace(line, string.Empty);

        var spans = new List<RunbookSpan>();
        var cursor = 0;

        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > cursor)
                spans.Add(new RunbookSpan(RunbookSpanKind.Text, text[cursor..match.Index]));

            spans.Add(match switch
            {
                { Groups: { } g } when g["bold"].Success
                    => new RunbookSpan(RunbookSpanKind.Bold, g["bold"].Value),
                { Groups: { } g } when g["code"].Success
                    => new RunbookSpan(RunbookSpanKind.Code, g["code"].Value),
                _ => new RunbookSpan(RunbookSpanKind.Text, match.Groups["link"].Value)
            });

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            spans.Add(new RunbookSpan(RunbookSpanKind.Text, text[cursor..]));

        return new RunbookLine(isStep, spans);
    }

    private static FrozenDictionary<string, RunbookEntry> Parse()
    {
        var text = ReadEmbedded();
        var headings = RuleHeading.Matches(text);
        var entries = new Dictionary<string, RunbookEntry>(headings.Count, StringComparer.Ordinal);

        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var start = heading.Index + heading.Length;

            // A section runs to the next rule heading, or to the end. Using the next *rule*
            // heading rather than the next "##" would swallow the trailing prose sections, so
            // the end of the document is only the fallback for the last rule.
            var end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;

            var body = text[start..end]
                .Split('\n')
                .Select(line => line.TrimEnd('\r').Trim())
                // Horizontal rules separate sections in the source and carry nothing.
                .Where(line => line.Length > 0 && line != "---")
                .ToList();

            var key = heading.Groups["key"].Value;
            entries[key] = new RunbookEntry(key, heading.Value.TrimStart('#').Trim(), body);
        }

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ReadEmbedded()
    {
        const string name = "LineOps.Reliability.runbook.md";

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"The runbook is not embedded in this assembly as '{name}'. "
                + "Check the EmbeddedResource item in LineOps.Reliability.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
