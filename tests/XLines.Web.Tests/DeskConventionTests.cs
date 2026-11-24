using System.Text.RegularExpressions;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The Desk's conventions, run against this application's source. Each guards a failure that
/// renders nothing to assert against, and each drifted here once: the stake box in every wager,
/// the opponent picker in a team window and the player finder in Parts shipped with no
/// accessible name; fourteen columns across eight panels declared a sort key and could never be
/// sorted; the template's Bootstrap sat in <c>wwwroot/lib</c>, 8 MB published with every build and
/// linked from nowhere. The checks themselves are the Desk's (<see cref="DeskConventions"/>); this
/// file is where XLines says which source, which head and which sheet are its own.
/// </summary>
public class DeskConventionTests
{
    private static readonly string Src = Path.Combine(Repo.Root(), "src");
    private static readonly string AppRazor = Path.Combine(Src, "XLines.Web", "Components", "App.razor");
    private static readonly string AppSheet = Path.Combine(Src, "XLines.Web", "wwwroot", "css", "xlines.css");

    /// <summary>All of them at once, as the Desk's README shows a consumer calling them.</summary>
    [Fact]
    public void The_desk_conventions_hold()
        => DeskConventions.All(Src, "XLines.Web/Components/App.razor", "XLines.Web/wwwroot/css/xlines.css");

    [Fact]
    public void Every_desk_field_has_a_name() => DeskConventions.EveryFieldHasAName(Src);

    [Fact]
    public void A_template_column_with_a_sort_key_is_sortable() => DeskConventions.SortKeysAreSortable(Src);

    [Fact]
    public void No_bootstrap_ships()
        => DeskConventions.NoBootstrapShips(Src, Path.Combine(Src, "XLines.Web", "wwwroot"));

    // This app's sheet declares no token block at all: every colour in it is the Desk's.
    [Fact]
    public void No_literal_colour_in_the_app_sheet() => DeskConventions.AppSheetSpendsTokens(AppSheet);

    // Rules that draw a target smaller than the floor on purpose, by selector exactly as the
    // sheet writes it, each with its reason. Empty: nothing on the desk is small on purpose yet.
    private static readonly Dictionary<string, string> SmallOnPurpose = new();

    [Fact]
    public void Targets_are_at_least_28px_in_the_app_sheet()
        => DeskConventions.AppSheetTargetsAreReachable(AppSheet, SmallOnPurpose);

    [Fact]
    public void The_app_sheet_does_not_fold_the_toolbar_again() => DeskConventions.AppSheetKeepsOneToolbarFold(AppSheet);

    [Fact]
    public void The_app_sheet_leaves_the_desk_s_components_alone()
        => DeskConventions.AppSheetLeavesDeskComponentsAlone(AppSheet);

    [Fact]
    public void The_app_links_its_sheet_after_deskhead() => DeskConventions.AppHeadLoadsDeskFirst(AppRazor, AppSheet);

    // ---- Spacing: the ramp, spent by the sheet and owned by the primitives -----------------

    private static readonly string Components = Path.Combine(Src, "XLines.Web", "Components");

    /// <summary>
    /// A window's markup writes no spacing of its own. The air between a window's parts belongs to
    /// the primitive or the sheet (PanelSection, the slots, EmptyState Compact, a class in
    /// xlines.css): an inline <c>margin:8px 0 0</c> on one snippet's note is a second rhythm
    /// nobody else can see or tune. Positional styles (a tick's <c>left</c>, a cell count) are
    /// layout data, not spacing, and stay.
    /// </summary>
    [Fact]
    public void No_window_writes_its_own_spacing_inline()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Components, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match style in Regex.Matches(text, @"\b[Ss]tyle=""(?<value>[^""]*)"""))
            {
                if (Regex.IsMatch(style.Groups["value"].Value, @"(?<![-\w])(margin|padding|gap|row-gap|column-gap)(-[a-z]+)?\s*:"))
                    offenders.Add($"{Path.GetRelativePath(Src, file)}:{text[..style.Index].Count(c => c == '\n') + 1}  {style.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Inline spacing in a window's markup:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// A toolbar's controls share one height and one type size: the search field's and the keys'
    /// 28px with body-size words. Every control in a PanelToolbar is drawn at its default size, so
    /// a window never sets a Small segmented control (footnote words, tighter segments) beside a
    /// default one in the next window, as the Journal, Performance, Players and Line movement once
    /// did beside the Board. A size is said nowhere inside a toolbar.
    ///
    /// <para>
    /// What this reads is the markup: a <c>Size=</c> attribute, quoted or not, written between a
    /// window's <c>&lt;PanelToolbar&gt;</c> and its close in the same file. A control that sizes
    /// itself inside a component of its own, placed in a toolbar, is not seen here — hence the
    /// name, which says no more than it checks.
    /// </para>
    /// </summary>
    [Fact]
    public void No_control_in_a_toolbars_markup_is_given_a_size()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Components, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match toolbar in Regex.Matches(text, @"<PanelToolbar\b.*?</PanelToolbar>", RegexOptions.Singleline))
            {
                foreach (Match size in Regex.Matches(toolbar.Value, @"\bSize\s*=\s*(""[^""]*""|'[^']*'|@[\w.()]+)"))
                    offenders.Add($"{Path.GetRelativePath(Src, file)}:{text[..(toolbar.Index + size.Index)].Count(c => c == '\n') + 1}  {size.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "A control sized inside a toolbar:\n" + string.Join("\n", offenders));
    }

    // Rules whose spacing is the geometry of something drawn beside it rather than a step of the
    // ramp, by selector exactly as the sheet writes it, each with its reason. Anything under 4px
    // (the ramp's first rung) is an optical nudge — a hairline's offset, a 2px inset — and is not
    // listed: the ramp has nothing that small to spend.
    private static readonly Dictionary<string, string> SpacedByGeometry = new()
    {
        [".board__notes--inset"] = "Indents past the small crest (18px) and its gap, so the notes start at the nicknames.",
        [".gamelines__book--crested"] = "Indents past the small crest (18px) and its gaps, so a book's name lines up with crested rows.",
        [".gamelines__detail"] = "Indents past the disclosure triangle (12px) and its gap, as controls.css indents a disclosure's content.",
        [".gc__plays"] = "Indents past the disclosure triangle (12px) and its gap, as controls.css indents a disclosure's content.",
        [".slip-badge"] = "A capsule on an 18px line: 5px sides keep a one-digit count round.",
    };

    /// <summary>
    /// The app's sheets space with the Desk's ramp (<c>--space-1</c> … <c>--space-16</c>), not with
    /// pixels: a margin, padding or gap of 4px or more is a token, or a calc of tokens. The sheets
    /// read are xlines.css and every window's scoped <c>.razor.css</c>.
    /// </summary>
    [Fact]
    public void The_app_sheets_space_with_the_ramp()
    {
        var sheets = new[] { AppSheet }
            .Concat(Directory.EnumerateFiles(Components, "*.razor.css", SearchOption.AllDirectories));
        var offenders = new List<string>();

        foreach (var sheet in sheets)
        {
            var css = DeskConventions.WithoutComments(File.ReadAllText(sheet));
            foreach (Match rule in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
            {
                var selector = rule.Groups["sel"].Value.Trim();
                if (SpacedByGeometry.ContainsKey(selector))
                    continue;

                foreach (Match declaration in Regex.Matches(rule.Groups["body"].Value,
                             @"(?<![-\w])(?<prop>(margin|padding)(-[a-z-]+)?|gap|row-gap|column-gap)\s*:\s*(?<value>[^;]+)"))
                {
                    var literals = Regex.Matches(declaration.Groups["value"].Value, @"(?<![\w.-])(?<n>\d+(\.\d+)?)(px|rem)\b")
                        .Where(m => double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 4);

                    if (literals.Any())
                        offenders.Add($"{Path.GetFileName(sheet)}: {selector} {{ {declaration.Value.Trim()} }}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Spacing written in pixels instead of the ramp:\n" + string.Join("\n", offenders));
    }

    // This app's xlines.css holds its windows: anything named after a window or a domain.
    [Theory]
    [InlineData(".price")]
    [InlineData(".board__")]
    [InlineData(".scoreboard")]
    [InlineData(".teammark")]
    [InlineData(".wager-")]
    [InlineData(".slip")]
    [InlineData(".journal-")]
    [InlineData(".ops")]
    [InlineData(".gamelines")]
    [InlineData(".perf-")]
    [InlineData(".movement")]
    [InlineData(".runbook")]
    [InlineData(".formrun")]
    [InlineData(".game-search")]
    [InlineData(".logrow__opp")]
    [InlineData(".snippet__market")]
    [InlineData(".wcard")]
    [InlineData(".stake-row")]
    [InlineData(".price-picker")]
    [InlineData(".selection-")]
    public void A_window_s_selector_lives_in_the_app_sheet(string selector)
        => DeskConventions.SelectorLivesInAppSheet(AppSheet, selector);
}
