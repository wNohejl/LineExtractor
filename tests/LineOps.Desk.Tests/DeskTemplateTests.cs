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

/// <summary>
/// A new application's agent reads the HIG skills the template brings. They have one source, this
/// repository's <c>.claude/skills</c>, which the template packs as it is built, so an edit to a
/// skill reaches the next template without a copy step. A copy kept inside the template's content
/// would go stale the first time a skill changed, so there is none, and every skill here is packed.
/// </summary>
public class DeskTemplateSkillsTests
{
    private static string Root => Repo.Root();

    public static TheoryData<string> Skills()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(Repo.Root(), ".claude", "skills")))
            data.Add(Path.GetFileName(dir));
        return data;
    }

    [Theory]
    [MemberData(nameof(Skills))]
    public void Every_skill_is_packed_from_its_one_source(string skill)
    {
        var packed = XDocument.Load(Path.Combine(Root, "src", "LineOps.Desk.Templates", "LineOps.Desk.Templates.csproj"))
            .Descendants("None")
            .Select(n => ((string?)n.Attribute("Include") ?? "").Replace('/', '\\'))
            .ToList();

        Assert.Contains($@"..\..\.claude\skills\{skill}\**\*", packed);
    }

    [Fact]
    public void The_template_keeps_no_copy_of_a_skill()
        => Assert.False(Directory.Exists(Path.Combine(Root, "src", "LineOps.Desk.Templates", "content", "deskapp", ".claude", "skills")),
            "The skills are packed from .claude/skills; a copy in the template's content would go stale.");
}
