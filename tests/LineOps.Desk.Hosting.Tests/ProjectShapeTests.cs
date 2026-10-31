using System.Xml.Linq;
using LineOps.Desk.Hosting.Tests.Support;

namespace LineOps.Desk.Hosting.Tests;

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
            Repo.Root(), "tests", "LineOps.Desk.Hosting.Tests", "LineOps.Desk.Hosting.Tests.csproj"));

        var references = csproj.Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include") ?? string.Empty)
            .ToList();

        var only = Assert.Single(references);
        Assert.EndsWith("LineOps.Desk.Hosting.csproj", only);
    }
}
