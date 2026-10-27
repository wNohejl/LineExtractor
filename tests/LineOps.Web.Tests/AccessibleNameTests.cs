using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// A field with only a placeholder has no name once someone types in it, and a field with
/// nothing at all is announced as "edit text" — the stake box in every wager, the opponent
/// picker in a team window and the player finder in Parts were all three. DeskField renders
/// bare when it has no Label, which is right for the fields sitting in a row, but it means
/// nothing stops a call site from shipping a nameless control. Read from the source, because
/// the gap is the absence of an attribute and renders nothing to assert against.
/// </summary>
public partial class AccessibleNameTests
{
    [GeneratedRegex(@"<(DeskField|DeskSelect|DeskTextArea|DeskSearch)\b")]
    private static partial Regex FieldTagStart();

    private static readonly string[] Names = ["Label=", "aria-label=", "aria-labelledby="];

    [Fact]
    public void Every_desk_field_has_a_name()
    {
        var root = Repo.Root();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (Match start in FieldTagStart().Matches(text))
            {
                var tag = OpeningTag(text, start.Index);
                var component = start.Groups[1].Value;

                // A search field's placeholder is its name: DeskSearch makes it the aria-label.
                var named = Names.Any(tag.Contains)
                            || (component == "DeskSearch" && tag.Contains("Placeholder="));

                if (!named)
                {
                    var line = text[..start.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line} <{component}>");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Desk field with no Label, aria-label or aria-labelledby (it has no accessible name):\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The opening tag up to the <c>&gt;</c> that closes it — not the first one, because an
    /// attribute such as <c>@onchange="@(e =&gt; …)"</c> holds a lambda arrow.
    /// </summary>
    private static string OpeningTag(string text, int from)
    {
        var quoted = false;
        var depth = 0;

        for (var i = from + 1; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
                quoted = !quoted;
            else if (quoted)
                continue;
            else if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
            else if (c == '>' && depth == 0)
                return text[from..(i + 1)];
        }

        return text[from..];
    }
}
