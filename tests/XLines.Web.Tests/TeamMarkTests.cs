using Bunit;
using XLines.Web.Components.Desk;
using XLines.Web.Tests.Support;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// A team's mark: the club's crest beside its name where ESPN serves one, a disc in the club's
/// colour where it does not, and nothing for a club the desk knows neither for. Team identity
/// is content, not chrome, so it lives in two C# maps and reaches the page only through this
/// component.
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

    public static readonly string[] NbaAbbrevs =
    [
        "ATL", "BKN", "BOS", "CHA", "CHI", "CLE", "DAL", "DEN", "DET", "GS", "HOU", "IND",
        "LAC", "LAL", "MEM", "MIA", "MIL", "MIN", "NO", "NY", "OKC", "ORL", "PHI", "PHX",
        "POR", "SA", "SAC", "TOR", "UTAH", "WSH",
    ];

    [Theory]
    [InlineData("nfl", "KC", "#E31837")]
    [InlineData("mlb", "NYY", "#0C2340")]
    [InlineData("NFL", "kc", "#E31837")]
    public void Known_team_has_its_primary_colour(string sport, string abbrev, string hex) =>
        Assert.Equal(hex, TeamColors.For(sport, abbrev));

    [Theory]
    [InlineData("nfl", "KC", "nfl", "kc")]
    [InlineData("NBA", "utah", "nba", "utah")]
    [InlineData("mlb", "CHW", "mlb", "chw")]
    public void A_club_with_a_crest_draws_it_for_each_ground(string sport, string abbrev, string league, string club)
    {
        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, sport)
            .Add(m => m.Abbrev, abbrev)
            .Add(m => m.Size, TeamMarkSize.Large));

        var mark = cut.Find("span.teammark.teammark--crest");
        Assert.Contains("teammark--lg", mark.ClassList);
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));

        var dark = cut.Find("img.teammark__img--dark");
        var light = cut.Find("img.teammark__img--light");
        Assert.Equal($"https://a.espncdn.com/combiner/i?img=/i/teamlogos/{league}/500-dark/{club}.png&h=84&w=84", dark.GetAttribute("src"));
        Assert.Equal($"https://a.espncdn.com/combiner/i?img=/i/teamlogos/{league}/500/{club}.png&h=84&w=84", light.GetAttribute("src"));

        // Decorative and lazy: an empty alt, so a crest that fails draws nothing and the name
        // beside it is the only thing read; lazy, so the hidden ground's file is never fetched.
        Assert.All(new[] { dark, light }, img =>
        {
            Assert.Equal(string.Empty, img.GetAttribute("alt"));
            Assert.Equal("lazy", img.GetAttribute("loading"));
            Assert.Equal("28", img.GetAttribute("width"));
        });
    }

    [Fact]
    public void A_coloured_club_with_no_crest_draws_its_disc()
    {
        // MLB's own spelling of Arizona has a colour, and ESPN serves no crest under it.
        Assert.False(TeamLogos.Has("mlb", "AZ"));

        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, "mlb")
            .Add(m => m.Abbrev, "AZ"));

        var mark = cut.Find("span.teammark.teammark--disc");
        Assert.Contains("teammark--sm", mark.ClassList);
        Assert.Equal("--team:#A71930", mark.GetAttribute("style"));
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Equal(string.Empty, mark.TextContent);
        Assert.Empty(cut.FindAll("img"));
    }

    [Theory]
    [InlineData("nfl", "XYZ")]
    [InlineData("nhl", "NYR")]
    [InlineData("ufc", "KC")]
    [InlineData(null, "KC")]
    [InlineData("nfl", null)]
    public void Unknown_team_renders_nothing(string? sport, string? abbrev)
    {
        Assert.Null(TeamColors.For(sport!, abbrev!));
        Assert.Null(TeamLogos.For(sport, abbrev, onDark: true, 54));

        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, sport)
            .Add(m => m.Abbrev, abbrev));

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Theory]
    [InlineData(TeamMarkSize.Small, "teammark--sm", "18")]
    [InlineData(TeamMarkSize.Large, "teammark--lg", "28")]
    [InlineData(TeamMarkSize.Hero, "teammark--hero", "44")]
    public void A_size_sets_the_class_and_the_box(TeamMarkSize size, string cls, string box)
    {
        var cut = RenderComponent<TeamMark>(p => p
            .Add(m => m.Sport, "nfl")
            .Add(m => m.Abbrev, "PIT")
            .Add(m => m.Size, size));

        Assert.Contains(cls, cut.Find("span.teammark").ClassList);
        Assert.All(cut.FindAll("img"), img => Assert.Equal(box, img.GetAttribute("height")));
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

    [Fact]
    public void Every_abbrev_the_desk_uses_has_a_crest()
    {
        Assert.Equal(30, NbaAbbrevs.Distinct().Count());

        var missing = NflAbbrevs.Where(a => !TeamLogos.Has("nfl", a)).Select(a => "nfl " + a)
            .Concat(MlbAbbrevs.Where(a => !TeamLogos.Has("mlb", a)).Select(a => "mlb " + a))
            .Concat(NbaAbbrevs.Where(a => !TeamLogos.Has("nba", a)).Select(a => "nba " + a))
            .ToList();

        Assert.True(missing.Count == 0, "No crest for: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_edge_lets_images_in_from_the_crest_host_and_nowhere_else()
    {
        var program = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Program.cs"));
        Assert.Contains("edge.ExtraImageSources.Add(XLines.Web.Components.Desk.TeamLogos.Origin)", program);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"ExtraImageSources\.Add"));
    }
}
