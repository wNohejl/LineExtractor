using Bunit;
using Bunit.TestDoubles;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// The two pages a host shows when the desk cannot: a request that failed, and an address that
/// opens nothing. Each says what happened in a heading and a sentence, and offers the one way
/// back — the desk's home, loaded fresh.
/// </summary>
public class DeskErrorPageTests : DeskTestContext
{
    [Fact]
    public void Error_page_says_what_happened_and_offers_the_way_back()
    {
        var cut = RenderComponent<DeskErrorPage>();

        Assert.Equal("Something went wrong", cut.Find("main.deskpage h1.deskpage__title").TextContent.Trim());

        var empty = cut.Find(".deskpage .empty.empty--error");
        Assert.Contains("The desk could not finish that request. Going back to it usually works.", empty.TextContent);
        Assert.Equal("Back to the desk", empty.QuerySelector(".empty__action button")!.TextContent.Trim());
    }

    [Fact]
    public void The_reference_shows_only_when_known()
    {
        var without = RenderComponent<DeskErrorPage>();
        Assert.Empty(without.FindAll(".deskpage__ref"));

        var with = RenderComponent<DeskErrorPage>(p => p.Add(x => x.RequestId, "00-abc-01"));
        var reference = with.Find(".deskpage__ref");
        Assert.Contains("Reference", reference.TextContent);
        Assert.Equal("00-abc-01", reference.QuerySelector("code")!.TextContent);
    }

    [Fact]
    public void Not_found_names_no_such_address()
    {
        var cut = RenderComponent<DeskNotFound>();

        Assert.Equal("Nothing here", cut.Find("main.deskpage h1.deskpage__title").TextContent.Trim());

        var empty = cut.Find(".deskpage .empty.empty--filtered");
        Assert.Contains("That address does not open anything on this desk.", empty.TextContent);
        Assert.Equal("Back to the desk", empty.QuerySelector(".empty__action button")!.TextContent.Trim());
    }

    [Fact]
    public void The_way_back_goes_home()
    {
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/somewhere/else");

        var cut = RenderComponent<DeskNotFound>();
        cut.Find(".empty__action button").Click();

        Assert.EndsWith("/", nav.Uri);
        Assert.True(nav.History.First().Options.ForceLoad);
    }

    [Fact]
    public void Copy_and_destination_can_be_named()
    {
        var nav = Services.GetRequiredService<FakeNavigationManager>();

        var cut = RenderComponent<DeskErrorPage>(p => p
            .Add(x => x.Title, "That did not finish")
            .Add(x => x.HomeHref, "/start")
            .Add(x => x.HomeLabel, "Start again")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "Try the start page."))));

        Assert.Equal("That did not finish", cut.Find("h1").TextContent.Trim());
        Assert.Contains("Try the start page.", cut.Find(".empty").TextContent);
        Assert.DoesNotContain("Going back to it usually works.", cut.Markup);

        cut.Find(".empty__action button").Click();
        Assert.EndsWith("/start", nav.Uri);
    }
}
