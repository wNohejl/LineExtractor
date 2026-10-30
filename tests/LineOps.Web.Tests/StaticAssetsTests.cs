using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// What the app ships beside its pages. The template's Bootstrap sat in <c>wwwroot/lib</c> long
/// after the desk stopped drawing with it — 8 MB published with every build and linked from
/// nowhere. Read from the source, because an unused file renders nothing to assert against.
/// </summary>
public class StaticAssetsTests
{
    private static readonly string[] Text = [".razor", ".cs", ".cshtml", ".html", ".css", ".js", ".json", ".csproj", ".props", ".targets"];

    [Fact]
    public void No_bootstrap_ships()
    {
        var root = Repo.Root();
        var src = Path.Combine(root, "src");

        Assert.False(Directory.Exists(Path.Combine(src, "LineOps.Web", "wwwroot", "lib")),
            "src/LineOps.Web/wwwroot/lib ships, and nothing on the desk links to it.");

        var references = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => Text.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(f => File.ReadAllText(f).Contains("lib/bootstrap", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(references.Count == 0, "Files still reference lib/bootstrap:\n" + string.Join("\n", references));
    }
}
