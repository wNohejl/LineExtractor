using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// A hover title on something that cannot take focus is read by a mouse and nobody else:
/// keyboard and touch never raise it, and screen readers mostly skip it. So on a span, a div, a
/// cell, an option or a paragraph, a title may only repeat what the element already shows — the
/// whole of a cell its column truncates. Anything it would add is visible text, or a DeskTooltip
/// on a trigger that takes focus. Buttons and links take focus, so their titles are reachable.
/// </summary>
public class TitleConventionTests
{
    private static readonly Regex Element = new(
        @"<(span|div|td|th|option|p|li)\b([^>]*?)\btitle=""([^""]*)""([^>]*)>",
        RegexOptions.Singleline);

    [Fact]
    public void A_title_on_text_that_cannot_take_focus_only_repeats_that_text()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            foreach (Match m in Element.Matches(source))
            {
                var tag = m.Groups[1].Value;
                var title = m.Groups[3].Value.Trim();
                var attributes = m.Groups[2].Value + m.Groups[4].Value;

                if (title.Length == 0 || attributes.Contains("tabindex"))
                    continue;

                var after = source[(m.Index + m.Length)..];
                var close = after.IndexOf($"</{tag}>", StringComparison.Ordinal);
                var inner = close < 0 ? after : after[..close];

                if (!inner.Contains(title, StringComparison.Ordinal))
                {
                    var line = source[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line} <{tag} title=\"{title}\">");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These titles say something their element does not show, to a mouse only:\n" + string.Join("\n", offenders));
    }
}
