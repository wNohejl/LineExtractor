using System.Reflection;
using System.Text.RegularExpressions;
using LineOps.Reliability;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Holds the runbook and the alert engine to each other.
///
/// This exists because they drifted. <c>budget_pressure</c> shipped as a documented rule with a
/// full triage section while <see cref="AlertEngine"/> had no code that could ever raise it —
/// the rule was unreachable, and nothing failed. A runbook entry for an alert that cannot fire
/// is worse than no entry at all: it is a procedure that will never be followed, and it makes
/// the documentation untrustworthy for the entries that are real.
///
/// So the relationship is asserted in both directions rather than trusted.
/// </summary>
public class RunbookCoverageTests
{
    /// <summary>Rule headings look like "## `freshness` — Critical". The backticked key is the contract.</summary>
    private static readonly Regex RuleHeading = new(@"^##\s+`(?<key>[a-z_]+)`", RegexOptions.Multiline);

    private static string RunbookPath
        => Path.Combine(AppContext.BaseDirectory, "runbook.md");

    /// <summary>Every public const string on <see cref="AlertRules"/>, read rather than restated.</summary>
    private static IReadOnlyList<string> DeclaredRules
        => typeof(AlertRules)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(k => k)
            .ToList();

    private static IReadOnlyList<string> DocumentedRules
        => RuleHeading.Matches(File.ReadAllText(RunbookPath))
            .Select(m => m.Groups["key"].Value)
            .OrderBy(k => k)
            .ToList();

    [Fact]
    public void TheRunbookIsWhereTheTestExpectsIt()
    {
        // Fail loudly rather than let a missing file turn the assertions below into vacuous passes.
        Assert.True(File.Exists(RunbookPath),
            $"runbook.md was not copied to the test output ({RunbookPath}). Check LineOps.Tests.csproj.");
    }

    [Fact]
    public void EveryAlertRuleHasARunbookSection()
    {
        var missing = DeclaredRules.Except(DocumentedRules).ToList();

        Assert.True(missing.Count == 0,
            $"Alert rules with no runbook section: {string.Join(", ", missing)}. "
            + "An on-call engineer paged by these has nothing to follow.");
    }

    [Fact]
    public void EveryDocumentedRuleIsARealAlertRule()
    {
        var orphaned = DocumentedRules.Except(DeclaredRules).ToList();

        Assert.True(orphaned.Count == 0,
            $"Runbook sections for rules that do not exist: {string.Join(", ", orphaned)}. "
            + "This is the drift that let budget_pressure look implemented for as long as it did.");
    }

    [Fact]
    public void AlertRulesAreNotEmpty()
    {
        // Guards the two assertions above: if reflection silently returned nothing, both would
        // pass against an empty set and the whole test class would be decorative.
        Assert.NotEmpty(DeclaredRules);
        Assert.NotEmpty(DocumentedRules);
    }
}
