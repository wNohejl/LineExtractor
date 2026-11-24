using System.Reflection;
using System.Text.RegularExpressions;
using XLines.Core.Contracts;
using XLines.Data;
using Microsoft.EntityFrameworkCore;

namespace XLines.Tests.Reliability;

/// <summary>
/// A new reader must ask (ADR 0024). A panel or service that reads a provider-supplied table and
/// never takes an <see cref="IDataTier"/> shows a visitor to a shared desk whatever ESPN supplied,
/// as a control that forgot <c>CanOperate</c> lets a visitor write (ADR 0018). Read from the source,
/// the way the Web suite's convention tests read it, because "queries the table" is a fact about
/// the code, not about the types.
///
/// <para>
/// The rule is deliberately plain: a file under the readers' folders that names one of the tiered
/// sets — every <c>DbSet</c> on <see cref="XLinesDbContext"/> whose rows are
/// <see cref="IFromSource"/>, read by reflection so a new table is covered the day it is added, plus
/// the two ESPN-only tables that carry no source — must also name <see cref="IDataTier"/>. It does
/// not prove the reader filters every query; it proves the reader was written by someone who had
/// to decide. The tests beside it prove the deciding was right.
/// </para>
/// </summary>
public class DataTierGuardTests
{
    /// <summary>
    /// Tables a personal-use source writes that have no <c>SourceId</c> to filter on, because only
    /// ESPN writes them (ADR 0021, ADR 0024 amendment). A reader of these asks the tier yes or no.
    /// </summary>
    private static readonly string[] SingleSourcePersonalUse = ["FighterProfiles", "Bouts"];

    /// <summary>
    /// Where readers live: the cross-reference and gamecast services, and the desk's panels and
    /// services. The ingestion and reliability projects are writers and the Worker's jobs — they
    /// read what they write, for no visitor — and are not scanned.
    /// </summary>
    private static readonly string[] ReaderFolders =
    [
        Path.Combine("src", "XLines.Data"),
        Path.Combine("src", "XLines.Web")
    ];

    /// <summary>
    /// Files under the reader folders that touch a tiered set and do not ask, each with why. Keep
    /// it short: an entry here is a reader a visitor can reach without the tier.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // Writes the seed and repairs on start-up; no person reads through it.
        [Path.Combine("src", "XLines.Data", "DatabaseInitializer.cs")] = "writer",
        // The context declares the sets; it reads nothing for anyone.
        [Path.Combine("src", "XLines.Data", "XLinesDbContext.cs")] = "declares the sets",
    };

    private static IReadOnlyList<string> TieredSets()
        => typeof(XLinesDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                        && typeof(IFromSource).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]))
            .Select(p => p.Name)
            .Concat(SingleSourcePersonalUse)
            .OrderBy(n => n)
            .ToList();

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "XLines.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("XLines.slnx not found above the test output.");
    }

    private static IEnumerable<string> ReaderFiles(string root)
        => ReaderFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_reader_of_a_tiered_table_takes_the_tier()
    {
        var root = Root();
        var sets = new Regex(@"\.(" + string.Join('|', TieredSets()) + @")\b", RegexOptions.Compiled);

        var offenders = ReaderFiles(root)
            .Select(f => (Path: Path.GetRelativePath(root, f), Text: File.ReadAllText(f)))
            .Where(f => !Allowed.ContainsKey(f.Path))
            .Where(f => sets.IsMatch(f.Text) && !f.Text.Contains(nameof(IDataTier), StringComparison.Ordinal))
            .Select(f => $"{f.Path} reads {sets.Match(f.Text).Groups[1].Value}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These read a provider-supplied table without asking the tier (ADR 0024): "
            + string.Join("; ", offenders)
            + ". Take an IDataTier and narrow with FromVisible (or PersonalUseColumns), or add the file "
            + "to the allow-list with the reason no visitor can reach it.");
    }

    /// <summary>
    /// Guards the guard: if reflection found no sets, or the scan found no readers, the rule above
    /// would pass against nothing.
    /// </summary>
    [Fact]
    public void The_guard_sees_the_tables_and_the_readers()
    {
        var sets = TieredSets();
        Assert.Contains("OddsSnapshots", sets);
        Assert.Contains("ClosingLines", sets);
        Assert.Contains("PlayerGameStats", sets);
        Assert.Contains("GamePlays", sets);
        Assert.Contains("FighterProfiles", sets);

        var root = Root();
        var files = ReaderFiles(root).Select(f => Path.GetRelativePath(root, f)).ToList();
        Assert.Contains(Path.Combine("src", "XLines.Data", "CrossReference", "BoardService.cs"), files);
        Assert.Contains(Path.Combine("src", "XLines.Web", "Components", "Panels", "PlayersPanel.razor"), files);

        foreach (var allowed in Allowed.Keys)
            Assert.True(File.Exists(Path.Combine(root, allowed)), $"The allow-list names {allowed}, which no longer exists.");
    }
}
