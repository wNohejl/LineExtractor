using System.Text.RegularExpressions;

namespace LineOps.Web.Tests;

/// <summary>
/// MudBlazor's <c>TemplateColumn</c> is not sortable unless it says so, whatever <c>SortBy</c> it
/// is given — and nothing warns. Fourteen columns across eight panels declared a sort key and
/// could never be sorted: the board by start or value, a head-to-head by result, the journal by
/// CLV. Read from the source, because the failure renders nothing to assert against.
/// </summary>
public partial class GridConventionTests
{
    [GeneratedRegex(@"<TemplateColumn\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex OpeningTag();

    [Fact]
    public void A_template_column_with_a_sort_key_is_sortable()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (Match tag in OpeningTag().Matches(text))
            {
                if (tag.Value.Contains("SortBy=") && !tag.Value.Contains("Sortable=\"true\""))
                {
                    var line = text[..tag.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "TemplateColumn with SortBy but not Sortable=\"true\" (it will never sort):\n" + string.Join("\n", offenders));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("LineOps.slnx not found above the test output.");
    }
}
