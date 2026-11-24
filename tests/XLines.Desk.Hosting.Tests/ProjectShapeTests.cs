using System.Xml.Linq;
using XLines.Desk.Hosting.Tests.Support;

namespace XLines.Desk.Hosting.Tests;

/// <summary>
/// These tests prove the Hosting package stands without the application, so they reference the
/// package and nothing else; the Desk comes with it. Read off the source, because a test that
/// compiled against the app would pass just the same.
/// </summary>
public class ProjectShapeTests
{
    [Fact]
    public void The_hosting_tests_reference_only_hosting()
    {
        var csproj = XDocument.Load(Path.Combine(
            Repo.Root(), "tests", "XLines.Desk.Hosting.Tests", "XLines.Desk.Hosting.Tests.csproj"));

        var references = csproj.Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include") ?? string.Empty)
            .ToList();

        var only = Assert.Single(references);
        Assert.EndsWith("XLines.Desk.Hosting.csproj", only);
    }
}
