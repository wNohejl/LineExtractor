using System.Reflection;
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
    /// <summary>Every public const string on <see cref="AlertRules"/>, read rather than restated.</summary>
    private static IReadOnlyList<string> DeclaredRules
        => typeof(AlertRules)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(k => k)
            .ToList();

    private static IReadOnlyList<string> DocumentedRules
        => Runbook.All.Keys.OrderBy(k => k).ToList();

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
        // Guards the two assertions above: if reflection or the embedded resource silently
        // returned nothing, both would pass against an empty set and this class would be
        // decorative — which is the exact failure it was written to prevent.
        Assert.NotEmpty(DeclaredRules);
        Assert.NotEmpty(DocumentedRules);
    }

    [Fact]
    public void EverySectionCarriesTriageStepsRatherThanJustADefinition()
    {
        // The panel renders these while an incident is being worked. A section that says what the
        // alert means but not what to do about it is not a runbook entry.
        foreach (var entry in Runbook.All.Values)
        {
            Assert.NotEmpty(entry.Body);
            Assert.Contains(entry.Body, line => line.Contains("**Triage**", StringComparison.Ordinal));
        }
    }
}
