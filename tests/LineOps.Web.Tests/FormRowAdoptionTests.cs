using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

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
        => Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    [Fact]
    public void No_form_row_is_spelled_by_hand()
    {
        var spelled = Sources().Where(s => Regex.IsMatch(s.Text, @"class=""wgroup__row wgroup__row--form")).Select(s => s.File);

        Assert.Empty(spelled);
    }

    [Fact]
    public void No_row_label_is_spelled_by_hand()
    {
        var spelled = Sources().Where(s => Regex.IsMatch(s.Text, @"class=""wgroup__label""")).Select(s => s.File);

        Assert.Empty(spelled);
    }
}
