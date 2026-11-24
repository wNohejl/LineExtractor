using Bunit;
using XLines.Data.CrossReference;
using XLines.Web.Components.Snippets;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// Where a club's crest goes beyond the mark itself: before a club's name wherever one is named,
/// at the size its context calls for, and laid out so a row with a crest in it is no taller than
/// one without.
/// </summary>
public class CrestPlacementTests : DeskTestContext
{
    private static readonly string Css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Web", "wwwroot", "css", "xlines.css"));

    [Fact]
    public void A_crest_in_a_line_lays_out_cap_high()
    {
        // The crest gives back everything it stands above the capitals, so the line box never
        // grows; measured against 1lh it gave nothing back while it was shorter than the line.
        Assert.Matches(@"\.teammark--crest\s*\{[^}]*margin-top:\s*calc\(1cap - var\(--teammark-size\)\);", Css);
        Assert.DoesNotMatch(@"\.teammark--crest\s*\{[^}]*margin-block:", Css);
    }

    [Fact]
    public void A_crest_heading_a_title_or_a_section_owns_its_height()
    {
        Assert.Matches(@"\.ptitle__name > \.teammark--crest,\s*\.panel-h > \.teammark--crest\s*\{[^}]*margin-top:\s*0;", Css);
        Assert.Matches(@"\.teammark--crest\.teammark--hero\s*\{[^}]*margin-top:\s*0;", Css);
    }

    [Fact]
    public void A_box_score_side_is_headed_by_its_clubs_large_crest()
    {
        var cut = RenderComponent<BoxScoreSide>(p => p
            .Add(s => s.Side, [new BoxScoreLine(7, "Josh Allen", "QB", false, new Dictionary<string, string> { ["passingYards"] = "301" })])
            .Add(s => s.Team, "Buffalo Bills")
            .Add(s => s.Sport, "nfl")
            .Add(s => s.Abbrev, "BUF"));

        var heading = cut.Find(".panel-h");
        Assert.NotNull(heading.QuerySelector(":scope > .teammark.teammark--crest.teammark--lg"));
        Assert.Equal("Buffalo Bills", heading.TextContent.Trim());
    }

    [Fact]
    public void A_fighters_box_score_side_has_no_crest()
    {
        var cut = RenderComponent<BoxScoreSide>(p => p
            .Add(s => s.Side, [new BoxScoreLine(9, "Alex Pereira", null, true, new Dictionary<string, string> { ["sigStrikes"] = "41" })])
            .Add(s => s.Team, "Alex Pereira")
            .Add(s => s.Sport, "ufc")
            .Add(s => s.Abbrev, "PEREIRA"));

        Assert.Empty(cut.FindAll(".teammark"));
    }
}
