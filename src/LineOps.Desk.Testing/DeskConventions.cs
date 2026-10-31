using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace LineOps.Desk.Testing;

/// <summary>
/// The Desk's house rules, as checks an application runs against its own source from its own
/// <c>[Fact]</c>s. Each one guards a failure that renders nothing to assert against — a field with
/// no accessible name, a column that declares a sort key and can never sort, a literal colour in a
/// sheet that should spend tokens, the application restyling a component the Desk draws — so they
/// read the source rather than a rendered page.
///
/// <para>
/// Every check takes the paths it reads. <c>sourceRoot</c> in the methods below is the
/// folder whose <c>.razor</c> files are the application's (and, in a repository that holds the
/// Desk beside it, the Desk's); the app razor is the file with <c>&lt;head&gt;</c> in it; the app
/// sheet is the application's own stylesheet, under its <c>wwwroot</c>. <see cref="All"/> runs
/// every check and reports every failure at once.
/// </para>
/// </summary>
public static partial class DeskConventions
{
    /// <summary>
    /// Every check, failures gathered rather than stopping at the first. <paramref name="appRazor"/>
    /// and <paramref name="appSheet"/> may be relative to <paramref name="sourceRoot"/>.
    /// </summary>
    public static void All(string sourceRoot, string appRazor, string appSheet,
        IReadOnlyDictionary<string, string>? smallOnPurpose = null)
    {
        appRazor = Path.Combine(sourceRoot, appRazor);
        appSheet = Path.Combine(sourceRoot, appSheet);
        var failures = new List<string>();

        void Check(string name, Action check)
        {
            try
            {
                check();
            }
            catch (XunitException e)
            {
                failures.Add($"{name}: {e.Message}");
            }
        }

        Check(nameof(EveryFieldHasAName), () => EveryFieldHasAName(sourceRoot));
        Check(nameof(SortKeysAreSortable), () => SortKeysAreSortable(sourceRoot));
        Check(nameof(NoBootstrapShips), () => NoBootstrapShips(sourceRoot, WebRoot(appSheet)));
        Check(nameof(AppSheetSpendsTokens), () => AppSheetSpendsTokens(appSheet));
        Check(nameof(AppSheetTargetsAreReachable), () => AppSheetTargetsAreReachable(appSheet, smallOnPurpose));
        Check(nameof(AppSheetKeepsOneToolbarFold), () => AppSheetKeepsOneToolbarFold(appSheet));
        Check(nameof(AppSheetLeavesDeskComponentsAlone), () => AppSheetLeavesDeskComponentsAlone(appSheet));
        Check(nameof(AppHeadLoadsDeskFirst), () => AppHeadLoadsDeskFirst(appRazor, appSheet));

        Assert.True(failures.Count == 0, "Desk conventions broken:\n\n" + string.Join("\n\n", failures));
    }

    /// <summary>
    /// The folder holding <paramref name="marker"/> (a solution file, say), searched for upward from
    /// the test assembly — the root a source check reads from, wherever the test output landed.
    /// </summary>
    public static string FindAbove(string marker)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, marker)) || Directory.Exists(Path.Combine(dir.FullName, marker)))
                return dir.FullName;
        }

        throw new InvalidOperationException($"{marker} not found above the test output.");
    }

    // ---- Fields ----------------------------------------------------------------------

    [GeneratedRegex(@"<(DeskField|DeskSelect|DeskTextArea|DeskSearch)\b")]
    private static partial Regex FieldTagStart();

    private static readonly string[] Names = ["Label=", "aria-label=", "aria-labelledby="];

    /// <summary>
    /// A field with only a placeholder has no name once someone types in it, and a field with
    /// nothing at all is announced as "edit text". DeskField renders bare when it has no Label,
    /// which is right for a field sitting in a row, but nothing stops a call site from shipping a
    /// nameless control — so every <c>DeskField</c>, <c>DeskSelect</c>, <c>DeskTextArea</c> and
    /// <c>DeskSearch</c> under <paramref name="sourceRoot"/> carries a Label, an aria-label or an
    /// aria-labelledby. A search field's placeholder is its name: DeskSearch makes it the aria-label.
    /// </summary>
    public static void EveryFieldHasAName(string sourceRoot)
    {
        var offenders = new List<string>();

        foreach (var file in Razor(sourceRoot))
        {
            var text = File.ReadAllText(file);

            foreach (Match start in FieldTagStart().Matches(text))
            {
                var tag = OpeningTag(text, start.Index);
                var component = start.Groups[1].Value;

                var named = Names.Any(tag.Contains)
                            || (component == "DeskSearch" && tag.Contains("Placeholder="));

                if (!named)
                    offenders.Add($"{Relative(sourceRoot, file)}:{Line(text, start.Index)} <{component}>");
            }
        }

        Assert.True(offenders.Count == 0,
            "Desk field with no Label, aria-label or aria-labelledby (it has no accessible name):\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The opening tag up to the <c>&gt;</c> that closes it — not the first one, because an
    /// attribute such as <c>@onchange="@(e =&gt; …)"</c> holds a lambda arrow.
    /// </summary>
    private static string OpeningTag(string text, int from)
    {
        var quoted = false;
        var depth = 0;

        for (var i = from + 1; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
                quoted = !quoted;
            else if (quoted)
                continue;
            else if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
            else if (c == '>' && depth == 0)
                return text[from..(i + 1)];
        }

        return text[from..];
    }

    // ---- Grids -----------------------------------------------------------------------

    [GeneratedRegex(@"<TemplateColumn\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TemplateColumnTag();

    /// <summary>
    /// MudBlazor's <c>TemplateColumn</c> is not sortable unless it says so, whatever <c>SortBy</c> it
    /// is given — and nothing warns. So a template column with a sort key says
    /// <c>Sortable="true"</c>.
    /// </summary>
    public static void SortKeysAreSortable(string sourceRoot)
    {
        var offenders = new List<string>();

        foreach (var file in Razor(sourceRoot))
        {
            var text = File.ReadAllText(file);

            foreach (Match tag in TemplateColumnTag().Matches(text))
            {
                if (tag.Value.Contains("SortBy=") && !tag.Value.Contains("Sortable=\"true\""))
                    offenders.Add($"{Relative(sourceRoot, file)}:{Line(text, tag.Index)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "TemplateColumn with SortBy but not Sortable=\"true\" (it will never sort):\n" + string.Join("\n", offenders));
    }

    // ---- What ships ------------------------------------------------------------------

    // Assembled, so that this file — which sits under a source root that is scanned — does not
    // name what it looks for.
    private static readonly string Bootstrap = string.Join("/", "lib", "bootstrap");

    private static readonly string[] Text = [".razor", ".cs", ".cshtml", ".html", ".css", ".js", ".json", ".csproj", ".props", ".targets"];

    /// <summary>
    /// The Blazor template's Bootstrap is 8 MB published with every build, and the Desk draws with
    /// none of it: the application's <c>wwwroot/lib</c> is gone, and nothing under
    /// <paramref name="sourceRoot"/> names the template's Bootstrap folder under <c>lib</c>.
    /// </summary>
    public static void NoBootstrapShips(string sourceRoot, string appWebRoot)
    {
        var lib = Path.Combine(appWebRoot, "lib");
        Assert.False(Directory.Exists(lib), $"{Relative(sourceRoot, lib)} ships, and nothing on the desk links to it.");

        var references = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(f => Text.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Built(f))
            .Where(f => File.ReadAllText(f).Contains(Bootstrap, StringComparison.OrdinalIgnoreCase))
            .Select(f => Relative(sourceRoot, f))
            .ToList();

        Assert.True(references.Count == 0, $"Files still reference {Bootstrap}:\n" + string.Join("\n", references));
    }

    // ---- The application's sheet -----------------------------------------------------

    private const string LiteralColour = @"#[0-9a-fA-F]{3,8}\b|rgba?\(";

    /// <summary>
    /// The application's sheet declares no token block: every colour in it is the Desk's, so a
    /// theme or an accent reaches it. A comment may name the colour it describes; only a rule can
    /// paint one.
    /// </summary>
    public static void AppSheetSpendsTokens(string appSheet)
        => Assert.DoesNotMatch(LiteralColour, WithoutComments(File.ReadAllText(appSheet)));

    /// <summary>
    /// A trigger, a segment or a takeable cell is something the pointer has to land on: 28px
    /// standing alone, 24px for a segment inside a gate, so nothing under 24px at all. Only the
    /// target itself is read, the last compound of each selector; an icon or a capsule inside one
    /// (<c>.x__trigger svg</c>) is decoration, not the target. <paramref name="smallOnPurpose"/>
    /// names rules that are small on purpose, by selector exactly as the sheet writes it, each with
    /// its reason.
    /// </summary>
    public static void AppSheetTargetsAreReachable(string appSheet, IReadOnlyDictionary<string, string>? smallOnPurpose = null)
    {
        var small = new List<string>();
        foreach (Match rule in Regex.Matches(WithoutComments(File.ReadAllText(appSheet)), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var selector = rule.Groups["sel"].Value.Trim();
            if (smallOnPurpose?.ContainsKey(selector) == true) continue;
            var targets = selector.Split(',')
                .Select(part => Regex.Split(part.Trim(), @"\s*[\s>+~]\s*").Last())
                .Any(last => Regex.IsMatch(last, @"__trigger|__opt|--take"));
            if (!targets) continue;
            foreach (Match height in Regex.Matches(rule.Groups["body"].Value, @"(?<![-\w])(?:min-)?height:\s*(?<px>\d+(?:\.\d+)?)px"))
            {
                if (double.Parse(height.Groups["px"].Value, CultureInfo.InvariantCulture) < 24)
                    small.Add($"{selector} {{ {height.Value} }}");
            }
        }
        Assert.Empty(small);
    }

    /// <summary>
    /// The window toolbar folds by one container query, the Desk's. A second one in the
    /// application's sheet lost the cascade to it once and folded the toolbar at the wrong width.
    /// </summary>
    public static void AppSheetKeepsOneToolbarFold(string appSheet)
        => Assert.DoesNotContain("@container ptoolbar (max-width: 520px)", File.ReadAllText(appSheet));

    /// <summary>
    /// The selectors the Desk's own components emit — DeskSteps, DeskTabs and DeskGrid among them.
    /// The rules that draw them travel with the package, so an application's sheet leaves them alone.
    /// </summary>
    public static IReadOnlyList<string> DeskComponentSelectors { get; } =
    [
        ".wsteps {",
        ".wstep__dot",
        ".wstep__title",
        ".wstep__change",
        ".wstep__hint",
        ".tabs__panel",
        ".deskgrid__child",
    ];

    /// <summary>The application's sheet restyles none of <see cref="DeskComponentSelectors"/>.</summary>
    public static void AppSheetLeavesDeskComponentsAlone(string appSheet)
    {
        var app = WithoutComments(File.ReadAllText(appSheet));
        foreach (var selector in DeskComponentSelectors)
            Assert.DoesNotContain(selector, app);
    }

    /// <summary>
    /// A selector named after one of the application's windows lives in the application's sheet
    /// and in none of the Desk's. Read with comments left out, because a comment may name a
    /// selector the other side owns.
    /// </summary>
    public static void SelectorLivesInAppSheet(string appSheet, string selector)
    {
        foreach (var sheet in DeskSheetNames)
            Assert.DoesNotContain(selector, WithoutComments(DeskSheet(sheet)));
        Assert.Contains(selector, WithoutComments(File.ReadAllText(appSheet)));
    }

    // ---- The application's head ------------------------------------------------------

    /// <summary>
    /// The application's <c>&lt;head&gt;</c> places <c>&lt;DeskHead /&gt;</c> before its own
    /// sheet and before any stylesheet at all, and links none of the Desk's sheets or scripts
    /// itself. DeskHead emits the theme boot script first and the Desk's sheets after it, so a
    /// stylesheet linked above it paints before the stored theme is stamped, and a sheet of the
    /// Desk's linked by hand loads twice or out of order.
    /// </summary>
    public static void AppHeadLoadsDeskFirst(string appRazor, string appSheet)
    {
        var app = File.ReadAllText(appRazor);
        Assert.DoesNotContain("_content/LineOps.Desk/css/", app);
        Assert.DoesNotContain("theme-boot.js", app);

        var deskHead = app.IndexOf("<DeskHead />", StringComparison.Ordinal);
        var own = app.IndexOf($"\"{SheetHref(appSheet)}\"", StringComparison.Ordinal);
        Assert.True(deskHead >= 0 && own > deskHead, $"DeskHead {deskHead}, app {own}");

        var firstSheet = app.IndexOf("<link rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.True(firstSheet < 0 || firstSheet > deskHead, $"DeskHead {deskHead}, first stylesheet {firstSheet}");
    }

    // ---- The Desk's sheets -----------------------------------------------------------

    /// <summary>The Desk's stylesheets, in the order DeskHead links them.</summary>
    public static IReadOnlyList<string> DeskSheetNames { get; } = ["desk.css", "controls.css", "themes.css", "mud-bridge.css"];

    /// <summary>One of the Desk's stylesheets as this package was built with it, comments and all.</summary>
    public static string DeskSheet(string name)
    {
        using var stream = typeof(DeskConventions).Assembly.GetManifestResourceStream($"LineOps.Desk.Testing.css.{name}")
            ?? throw new ArgumentException($"The Desk ships no sheet called {name}.", nameof(name));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A stylesheet with its <c>/* … */</c> comments removed.</summary>
    public static string WithoutComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    // ---- Reading the source ----------------------------------------------------------

    private static IEnumerable<string> Razor(string sourceRoot)
        => Directory.EnumerateFiles(sourceRoot, "*.razor", SearchOption.AllDirectories).Where(f => !Built(f));

    private static bool Built(string file)
        => file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static int Line(string text, int index) => text[..index].Count(c => c == '\n') + 1;

    // Relative to the folder above the source root, so a path reads "src/App/…" as it does in a diff.
    private static string Relative(string sourceRoot, string file)
        => Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar)) ?? sourceRoot, file);

    /// <summary>The <c>wwwroot</c> folder a sheet sits under.</summary>
    private static string WebRoot(string appSheet)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(appSheet)).Parent; dir is not null; dir = dir.Parent)
        {
            if (dir.Name == "wwwroot")
                return dir.FullName;
        }

        throw new ArgumentException($"{appSheet} is not under a wwwroot folder.", nameof(appSheet));
    }

    /// <summary>The sheet's address as the page links it: its path under <c>wwwroot</c>.</summary>
    private static string SheetHref(string appSheet)
        => Path.GetRelativePath(WebRoot(appSheet), Path.GetFullPath(appSheet)).Replace('\\', '/');
}
