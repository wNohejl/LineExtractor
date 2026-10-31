using System.Xml.Linq;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// <c>dotnet new deskapp</c> names the Desk's package versions in its own
/// <c>Directory.Packages.props</c>. A release that raised a package and left the template behind
/// would start every new application on the last one, and nothing would fail to say so.
/// </summary>
public class DeskTemplateTests
{
    [Theory]
    [InlineData("LineOps.Desk")]
    [InlineData("LineOps.Desk.Hosting")]
    public void The_template_starts_on_the_version_the_package_declares(string package)
    {
        var declared = (string?)XDocument.Load(Path.Combine(Repo.Root(), "src", package, package + ".csproj"))
            .Descendants("Version").Single();

        var pinned = XDocument.Load(Path.Combine(
                Repo.Root(), "src", "LineOps.Desk.Templates", "content", "deskapp", "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(p => (string?)p.Attribute("Include") == package)
            .Select(p => (string?)p.Attribute("Version"))
            .Single();

        Assert.Equal(declared, pinned);
    }
}
