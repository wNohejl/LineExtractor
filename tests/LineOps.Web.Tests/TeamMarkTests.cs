using Bunit;
using LineOps.Web.Components.Desk;

namespace LineOps.Web.Tests;

/// <summary>
/// A team's mark: a small disc in the club's colour beside its name. Team colour is content,
/// not chrome, so it lives in one C# map and reaches the page only through this component.
/// </summary>
public class TeamMarkTests : DeskTestContext
{
    // Every club as ESPN spells it, read from the Teams table the ESPN adapter writes.
    public static readonly string[] NflAbbrevs =
    [
        "ARI", "ATL", "BAL", "BUF", "CAR", "CHI", "CIN", "CLE", "DAL", "DEN", "DET", "GB",
        "HOU", "IND", "JAX", "KC", "LAC", "LAR", "LV", "MIA", "MIN", "NE", "NO", "NYG",
        "NYJ", "PHI", "PIT", "SEA", "SF", "TB", "TEN", "WSH",
    ];

    public static readonly string[] MlbAbbrevs =
    [
        "ARI", "ATH", "ATL", "BAL", "BOS", "CHC", "CHW", "CIN", "CLE", "COL", "DET", "HOU",
        "KC", "LAA", "LAD", "MIA", "MIL", "MIN", "NYM", "NYY", "PHI", "PIT", "SD", "SEA",
        "SF", "STL", "TB", "TEX", "TOR", "WSH",
    ];

    [Theory]
    [InlineData("nfl", "KC", "#E31837")]
    [InlineData("mlb", "NYY", "#0C2340")]
    [InlineData("NFL", "kc", "#E31837")]
    public void Known_team_has_its_primary_colour(string sport, string abbrev, string hex)
    {
        Assert.Equal(hex, TeamColors.For(sport, abbrev));

        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, sport)
            .Add(m => m.Abbrev, abbrev)
            .Add(m => m.Size, TeamMarkSize.Large));

        var mark = cut.Find("span.teammark");
        Assert.Contains("teammark--lg", mark.ClassList);
        Assert.Equal($"--team:{hex}", mark.GetAttribute("style"));
    }

    [Theory]
    [InlineData("nfl", "XYZ")]
    [InlineData("nba", "LAL")]
    [InlineData(null, "KC")]
    [InlineData("nfl", null)]
    public void Unknown_team_renders_nothing(string? sport, string? abbrev)
    {
        Assert.Null(TeamColors.For(sport!, abbrev!));

        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, sport)
            .Add(m => m.Abbrev, abbrev));

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void Mark_is_hidden_from_assistive_tech()
    {
        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, "nfl")
            .Add(m => m.Abbrev, "PIT"));

        var mark = cut.Find("span.teammark");
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Contains("teammark--sm", mark.ClassList);
        Assert.Equal(string.Empty, mark.TextContent);
    }

    [Fact]
    public void Every_abbrev_the_desk_uses_has_a_colour()
    {
        Assert.Equal(32, NflAbbrevs.Distinct().Count());
        Assert.Equal(30, MlbAbbrevs.Distinct().Count());

        var missing = NflAbbrevs.Where(a => TeamColors.For("nfl", a) is null).Select(a => "nfl " + a)
            .Concat(MlbAbbrevs.Where(a => TeamColors.For("mlb", a) is null).Select(a => "mlb " + a))
            .ToList();

        Assert.True(missing.Count == 0, "No colour for: " + string.Join(", ", missing));
        Assert.All(NflAbbrevs.Select(a => TeamColors.For("nfl", a)).Concat(MlbAbbrevs.Select(a => TeamColors.For("mlb", a))),
            hex => Assert.Matches("^#[0-9A-F]{6}$", hex));
    }
}
