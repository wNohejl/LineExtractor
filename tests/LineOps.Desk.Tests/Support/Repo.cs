namespace LineOps.Desk.Tests.Support;

/// <summary>The repository root, for tests that read source rather than render it.</summary>
public static class Repo
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("LineOps.slnx not found above the test output.");
    }
}
