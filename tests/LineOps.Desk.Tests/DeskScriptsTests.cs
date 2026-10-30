using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// The shape of the Desk's scripts where shape is the point. A browser fires resize many times a
/// frame while a window is dragged wider, and a page that rebuilds itself fires a mutation for
/// every node it adds; work done per event rather than per frame is work the eye never sees. There
/// is no script engine in the test run, so these read the source: crude, but they pin the shape.
/// </summary>
public class DeskScriptsTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "js", name));

    [Theory]
    [InlineData("windowing.js")]
    [InlineData("desk-glide.js")]
    [InlineData("dialogs.js")]
    public void Resize_work_is_coalesced_to_a_frame(string file)
    {
        var source = Script(file);

        var listener = Regex.Match(source, @"addEventListener\(\s*'resize'\s*,\s*(\w+)");
        Assert.True(listener.Success, $"{file} has no resize listener naming a function.");

        var body = FunctionBody(source, listener.Groups[1].Value);
        Assert.True(body is not null, $"{file}: the resize listener '{listener.Groups[1].Value}' is not a function declared in the file.");
        Assert.Contains("requestAnimationFrame", body);
    }

    [Fact]
    public void The_glide_s_observer_sweeps_once_per_frame_rather_than_per_added_node()
    {
        var source = Script("desk-glide.js");

        var start = source.IndexOf("new MutationObserver(", StringComparison.Ordinal);
        Assert.True(start >= 0, "desk-glide.js has no MutationObserver.");

        var callback = Block(source, source.IndexOf('{', start));

        // No query inside the callback: what was added is only flagged, and found by one sweep.
        Assert.DoesNotContain("querySelectorAll", callback);

        // The callback hands the finding to a function that waits for a frame.
        var called = Regex.Matches(callback, @"\b(\w+)\s*\(").Select(m => m.Groups[1].Value).Distinct();
        Assert.Contains(called, name => FunctionBody(source, name)?.Contains("requestAnimationFrame") == true);

        // That sweep leaves alone the hosts already seated, so a host is seated once on arrival.
        Assert.Contains("new WeakSet(", source);
    }

    /// <summary>The text of <c>function name(…) { … }</c>'s body, braces included, or null.</summary>
    private static string? FunctionBody(string source, string name)
    {
        var declaration = Regex.Match(source, @"function\s+" + Regex.Escape(name) + @"\s*\(");
        return declaration.Success ? Block(source, source.IndexOf('{', declaration.Index)) : null;
    }

    /// <summary>From an opening brace to its match. The scripts hold no brace in a string.</summary>
    private static string Block(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        throw new InvalidOperationException("Unbalanced braces.");
    }
}
