using Bunit;
using XLines.Data.Gamecast;
using XLines.Desk.Testing;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The Gamecast's field graphic: the field always, and once a side has the ball the ball, the
/// line of scrimmage, the line to gain and the drive played out on it — one segment a play — with
/// the play in words under it, never on the turf.
/// </summary>
public class FieldGraphicTests : DeskTestContext
{
    private static GamecastPlay Play(long sequence, int start, int end, bool penalty = false)
        => FakeGamecastData.Play(sequence, 1, "play") with
        {
            IsPenalty = penalty,
            Start = new GamecastSituation(1, 10, start, null, null, null, FakeGamecastData.AwayId),
            End = new GamecastSituation(2, 6, end, null, null, null, FakeGamecastData.AwayId),
        };

    private IRenderedComponent<FieldGraphic> Render(Action<ComponentParameterCollectionBuilder<FieldGraphic>>? more = null)
        => RenderComponent<FieldGraphic>(p =>
        {
            p.Add(s => s.HomeAbbrev, "WSH").Add(s => s.AwayAbbrev, "PHI").Add(s => s.Sport, "nfl")
                .Add(s => s.Label, "Field: PHI ball, 2nd and 6 at WSH 34");
            more?.Invoke(p);
        });

    [Fact]
    public void A_spotted_ball_draws_the_ball_the_line_to_gain_and_the_line_of_scrimmage()
    {
        var cut = Render(p => p.Add(s => s.YardLine, 34).Add(s => s.Distance, 6).Add(s => s.HomeBall, false));

        var svg = cut.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Field: PHI ball, 2nd and 6 at WSH 34", svg.GetAttribute("aria-label"));

        // WSH 34 is x 76; six to go rightward puts the line to gain at x 82.
        var ball = cut.Find(".fieldgraphic__ball");
        Assert.Equal("76", ball.GetAttribute("data-spot"));
        Assert.Contains("translateX(76px)", ball.GetAttribute("style"));
        Assert.Contains("translateX(82px)", cut.Find(".fieldgraphic__gain").GetAttribute("style"));
        Assert.Single(cut.FindAll(".fieldgraphic__scrim"));
        Assert.Empty(cut.FindAll(".fieldgraphic__red"));
    }

    [Fact]
    public void With_nobody_on_the_ball_the_field_draws_alone()
    {
        var cut = Render(p => p.Add(s => s.YardLine, 34).Add(s => s.Distance, 6)
            .Add(s => s.DrivePlays, [Play(1, 75, 70)]).Add(s => s.DriveStartYardLine, 75));

        Assert.Empty(cut.FindAll(".fieldgraphic__ball"));
        Assert.Empty(cut.FindAll(".fieldgraphic__gain"));
        Assert.Empty(cut.FindAll(".fieldgraphic__scrim"));
        Assert.Empty(cut.FindAll(".fieldgraphic__play"));
        Assert.Empty(cut.FindAll(".fieldgraphic__band"));
        Assert.Equal(2, cut.FindAll(".fieldgraphic__zone").Count);
        Assert.Equal(9, cut.FindAll(".fieldgraphic__yard").Count);
        Assert.Equal("0 0 120 32", cut.Find("svg").GetAttribute("viewBox"));
    }

    [Fact]
    public void Each_play_with_both_ends_is_one_segment()
    {
        var plays = new[]
        {
            Play(1, 75, 71),
            Play(2, 71, 71),
            FakeGamecastData.Play(3, 1, "no spot"),
            Play(4, 71, 66, penalty: true),
            Play(5, 66, 34),
        };

        var cut = Render(p => p.Add(s => s.YardLine, 34).Add(s => s.Distance, 10).Add(s => s.HomeBall, false)
            .Add(s => s.DrivePlays, plays).Add(s => s.DriveStartYardLine, 75));

        var segments = cut.FindAll(".fieldgraphic__play");
        Assert.Equal(4, segments.Count);
        Assert.Equal(new[] { "gain", "nogain", "penalty", "gain" }, segments.Select(s => s.GetAttribute("data-kind")));
        Assert.Contains("fieldgraphic__play--latest", segments[^1].ClassName);

        // The band runs from the drive's start (x 35) to the ball (x 76).
        var band = cut.Find(".fieldgraphic__band");
        Assert.Equal("35", band.GetAttribute("x"));
        Assert.Equal("41", band.GetAttribute("width"));
    }

    [Fact]
    public void The_red_zone_is_washed_once_the_ball_is_inside_it()
    {
        var cut = Render(p => p.Add(s => s.YardLine, 15).Add(s => s.Distance, 5).Add(s => s.HomeBall, false));

        Assert.Equal("90", cut.Find(".fieldgraphic__red").GetAttribute("x"));
    }

    [Fact]
    public void The_caption_says_the_situation_and_the_last_play_under_the_field()
    {
        var cut = Render(p => p.Add(s => s.YardLine, 34).Add(s => s.HomeBall, false)
            .Add(s => s.Situation, "2nd & 6 at WSH 34")
            .Add(s => s.LastPlay, "J.Hurts pass short right to A.Brown for 9 yards"));

        var caption = cut.Find("p.fieldgraphic__caption");
        Assert.Contains("2nd &amp; 6 at WSH 34", caption.InnerHtml);
        Assert.Contains("PHI", cut.Find(".fieldgraphic__side").TextContent);
        Assert.Equal("J.Hurts pass short right to A.Brown for 9 yards", cut.Find(".fieldgraphic__lastplay").TextContent);

        // The play in words is under the field, not on it: the SVG's text is numbers and abbreviations.
        var onField = cut.FindAll("svg text").Select(t => t.TextContent.Trim()).ToList();
        Assert.All(onField, t => Assert.True(int.TryParse(t, out _) || t is "PHI" or "WSH", t));
    }

    [Fact]
    public void With_neither_the_situation_nor_a_play_there_is_no_caption()
    {
        var cut = Render(p => p.Add(s => s.YardLine, 34).Add(s => s.HomeBall, true).Add(s => s.Situation, " "));

        Assert.Empty(cut.FindAll(".fieldgraphic__caption"));
    }

    [Fact]
    public void The_sheet_draws_no_outline_spends_no_accent_and_stops_under_reduced_motion()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "Components", "Snippets", "FieldGraphic.razor.css"));

        Assert.DoesNotContain("border:", css);
        Assert.DoesNotContain("--accent", css);
        Assert.DoesNotContain("animation", css);
        Assert.Contains("max-width: 760px", css);
        Assert.Contains("prefers-reduced-motion: reduce", css);
    }
}
