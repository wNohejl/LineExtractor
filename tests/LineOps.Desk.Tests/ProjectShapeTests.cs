using System.Text.RegularExpressions;
using System.Xml.Linq;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// The Desk's tests prove the Desk stands without the application, so they must not reach the
/// application at all — not through their own reference, and not through a transitive one that
/// a reference to the app would bring in. Both are read off the source, because a test that
/// compiled against the app would pass just the same.
/// </summary>
public class ProjectShapeTests
{
    private static string ProjectDirectory() => Path.Combine(Repo.Root(), "tests", "LineOps.Desk.Tests");

    /// <summary>One reference, and it is the Desk.</summary>
    [Fact]
    public void The_desk_tests_reference_only_the_desk()
    {
        var csproj = XDocument.Load(Path.Combine(ProjectDirectory(), "LineOps.Desk.Tests.csproj"));

        var references = csproj.Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include") ?? string.Empty)
            .ToList();

        var only = Assert.Single(references);
        Assert.EndsWith("LineOps.Desk.csproj", only);
    }

    /// <summary>
    /// No test names an application namespace. A quoted name is a path segment or the text an
    /// assertion looks for (a path into the app's sources, a check that a Desk file does not
    /// mention the app), not a reference, so it is let through.
    /// </summary>
    [Fact]
    public void No_moved_test_names_the_app()
    {
        string[] app = ["LineOps.Web", "LineOps.Core", "LineOps.Data"];
        var reference = new Regex(@"\b(" + string.Join("|", app.Select(Regex.Escape)) + @")\b(?!"")");
        var separator = Path.DirectorySeparatorChar;

        var offenders = Directory.EnumerateFiles(ProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}bin{separator}") && !f.Contains($"{separator}obj{separator}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => reference.IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(ProjectDirectory(), l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Desk tests that name the app:\n" + string.Join("\n", offenders));
    }
}
