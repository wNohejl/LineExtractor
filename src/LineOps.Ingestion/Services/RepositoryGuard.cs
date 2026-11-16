using System.Diagnostics;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Refuses a personal file a git repository would pick up. The repository is public, and a bet
/// log written where <c>git add</c> sees it is one commit from being published. A path outside
/// any repository, or one the repository ignores (<c>data/personal/</c>), is allowed.
/// </summary>
public static class RepositoryGuard
{
    /// <summary>Why <paramref name="path"/> may not be written, or null when it may.</summary>
    public static string? Refusal(string path)
    {
        var full = Path.GetFullPath(path);
        var root = RepositoryOf(full);

        if (root is null)
            return null;

        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');

        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in new[] { "check-ignore", "-q", "--", relative })
                start.ArgumentList.Add(arg);

            using var git = Process.Start(start)!;
            git.WaitForExit(15_000);

            return git.HasExited && git.ExitCode == 0
                ? null
                : $"{full} is inside the repository at {root} and git does not ignore it. The repository is public: "
                  + "write the journal under data/personal/ (ignored), or outside the repository.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No git to ask: inside a repository, fail closed.
            return $"{full} is inside the repository at {root}, and git could not be asked whether it is ignored ({ex.Message}).";
        }
    }

    /// <summary>The nearest directory above the path holding a <c>.git</c> (a directory, or a worktree's file).</summary>
    internal static string? RepositoryOf(string fullPath)
    {
        for (var dir = Path.GetDirectoryName(fullPath); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
        }

        return null;
    }
}
