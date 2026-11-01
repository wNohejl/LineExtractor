using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The grouped form has a primitive (<c>FormGroup</c> and <c>FormRow</c> in the Desk) and an older
/// spelling: the four class names written by hand. The spelling is not wrong, so this does not
/// forbid it; it ratchets. The count of hand-spelled form rows in this application may only go
/// down — a window that is touched converts its rows, and nothing new is written the old way.
/// Lower the numbers here as windows convert.
/// </summary>
public class FormRowAdoptionTests
{
    /// <summary>Hand-spelled form rows left on 1 November 2026, after the Slip converted.</summary>
    private const int FormRowsLeft = 40;

    /// <summary>Hand-spelled row labels left on the same day.</summary>
    private const int LabelsLeft = 35;

    private static IEnumerable<string> Sources()
        => Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

    [Fact]
    public void Hand_spelled_form_rows_only_go_down()
    {
        var rows = Sources().Sum(s => Regex.Matches(s, @"class=""wgroup__row wgroup__row--form").Count);

        Assert.True(rows <= FormRowsLeft,
            $"{rows} form rows are spelled by hand (the ratchet is {FormRowsLeft}). New rows are <FormRow Label=\"…\">; a window you touch converts its rows.");
    }

    [Fact]
    public void Hand_spelled_row_labels_only_go_down()
    {
        var labels = Sources().Sum(s => Regex.Matches(s, @"class=""wgroup__label""").Count);

        Assert.True(labels <= LabelsLeft,
            $"{labels} row labels are spelled by hand (the ratchet is {LabelsLeft}). A FormRow's Label is the label.");
    }
}
