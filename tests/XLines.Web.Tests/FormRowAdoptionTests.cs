using System.Text.RegularExpressions;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The grouped form has one spelling: <c>FormGroup</c> and <c>FormRow</c> in the Desk. The older
/// spelling, the class names written by hand, drifted window to window (a head that was an h4
/// here and a div there, a footer inside the group instead of under it), so it ratcheted down
/// from forty rows on 1 November 2026 to none on 7 November. A row whose label carries markup is
/// a FormRow with <c>LabelContent</c>; a row whose leading column is not a label is a FormRow
/// with <c>Lead</c>. A row that is not label-and-control at all is a plain <c>.wgroup__row</c>.
/// </summary>
public class FormRowAdoptionTests
{
    private static IEnumerable<(string File, string Text)> Sources()
        => Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    [Fact]
    public void No_form_row_is_spelled_by_hand()
    {
        var spelled = Sources().Where(s => Regex.IsMatch(s.Text, @"class=""wgroup__row wgroup__row--form")).Select(s => s.File);

        Assert.Empty(spelled);
    }

    /// <summary>
    /// A label leading a split row's cell (<c>.wgroup__split &gt; .wgroup__cell</c>, the Desk's
    /// grouping contract) is the one hand-spelled label: two compact controls in one row is not a
    /// FormRow, and the contract spells each cell's label as a <c>span.wgroup__label</c> first in
    /// its cell. Any other <c>wgroup__label</c> is a FormRow spelled by hand.
    /// </summary>
    [Fact]
    public void No_row_label_is_spelled_by_hand()
    {
        var spelled = Sources()
            .Where(s => Regex.Matches(s.Text, @"class=""wgroup__label""")
                .Any(m => !Regex.IsMatch(s.Text[..m.Index], @"<div class=""wgroup__cell"">\s*<span\s*$")))
            .Select(s => s.File);

        Assert.Empty(spelled);
    }
}
