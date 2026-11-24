using System.Reflection;
using Bunit;
using XLines.Data;
using XLines.Desk.Primitives;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The Board's empty state in the three states of the stats source (ADR 0026): ESPN on offers
/// Today's games as it always has; The Odds API on a key offers Fixtures and says it is free, and
/// that lines and scores are the metered pulls; neither sends the operator to Ops for a key. The
/// panel's first load fails here (no database), and the test places what a load would have read,
/// as the Ops tests do.
/// </summary>
public class BoardStatsSourceTests : WagerTestContext
{
    public BoardStatsSourceTests()
    {
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new UnreachableFactory());
        Services.AddSingleton<RangePull>();
        Services.AddScoped<XLines.Desk.DeskJobRunner>();
    }

    private IRenderedComponent<BoardPanel> Render(StatsPort? port)
    {
        var panel = RenderComponent<BoardPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".empty--error")));

        PanelProbe.MarkLoaded(panel.Instance);
        typeof(BoardPanel).GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel.Instance, port);

        panel.Render();
        return panel;
    }

    private static string Empty(IRenderedFragment panel)
        => string.Join(' ', panel.Find(".empty").TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void With_no_source_the_board_sends_the_operator_to_ops_for_a_key()
    {
        var panel = Render(StatsPort.None);

        Assert.Contains("enter your free Odds API key in Ops → Odds feeds", Empty(panel));
        Assert.Equal("Open Ops", panel.Find(".empty button").TextContent.Trim());
    }

    [Fact]
    public void With_the_odds_api_the_board_offers_fixtures_and_says_they_are_free()
    {
        var panel = Render(StatsPort.TheOddsApi);

        var text = Empty(panel);
        Assert.Contains("Fixtures fetches the next eight days from The Odds API and is free", text);
        Assert.Contains("Pull lines prices them and Pull scores finishes them, each for the credits Pull data states", text);

        var key = panel.Find(".empty button");
        Assert.Equal("Fixtures · free", key.TextContent.Trim());
    }

    [Fact]
    public void With_espn_the_board_offers_todays_games_as_before()
    {
        var panel = Render(StatsPort.Espn);

        Assert.Contains("Today's games fetches the schedule and the live scores from ESPN", Empty(panel));
        Assert.Equal("Today's games", panel.Find(".empty button").TextContent.Trim());
    }
}
