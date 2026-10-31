using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Desk's conventions, run against this application's source. Each guards a failure that
/// renders nothing to assert against, and each drifted here once: the stake box in every wager,
/// the opponent picker in a team window and the player finder in Parts shipped with no
/// accessible name; fourteen columns across eight panels declared a sort key and could never be
/// sorted; the template's Bootstrap sat in <c>wwwroot/lib</c>, 8 MB published with every build and
/// linked from nowhere. The checks themselves are the Desk's (<see cref="DeskConventions"/>); this
/// file is where LineOps says which source, which head and which sheet are its own.
/// </summary>
public class DeskConventionTests
{
    private static readonly string Src = Path.Combine(Repo.Root(), "src");
    private static readonly string AppRazor = Path.Combine(Src, "LineOps.Web", "Components", "App.razor");
    private static readonly string AppSheet = Path.Combine(Src, "LineOps.Web", "wwwroot", "css", "lineops.css");

    /// <summary>All of them at once, as the Desk's README shows a consumer calling them.</summary>
    [Fact]
    public void The_desk_conventions_hold()
        => DeskConventions.All(Src, "LineOps.Web/Components/App.razor", "LineOps.Web/wwwroot/css/lineops.css");

    [Fact]
    public void Every_desk_field_has_a_name() => DeskConventions.EveryFieldHasAName(Src);

    [Fact]
    public void A_template_column_with_a_sort_key_is_sortable() => DeskConventions.SortKeysAreSortable(Src);

    [Fact]
    public void No_bootstrap_ships()
        => DeskConventions.NoBootstrapShips(Src, Path.Combine(Src, "LineOps.Web", "wwwroot"));

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

    // This app's lineops.css holds its windows: anything named after a window or a domain.
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
    [InlineData(".splits")]
    [InlineData(".snippet__market")]
    [InlineData(".wcard")]
    [InlineData(".stake-row")]
    [InlineData(".book-picker")]
    [InlineData(".selection-")]
    public void A_window_s_selector_lives_in_the_app_sheet(string selector)
        => DeskConventions.SelectorLivesInAppSheet(AppSheet, selector);
}
