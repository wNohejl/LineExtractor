using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// The badge: a count beside the name of what it counts. Nothing to count is no badge at all,
/// a large count is capped so the pill keeps its width, and a reader hears the count and its noun
/// rather than a bare figure.
/// </summary>
public class DeskBadgeTests : DeskTestContext
{
    private IRenderedComponent<DeskBadge> Badge(int count, int? max = null)
        => RenderComponent<DeskBadge>(p =>
        {
            p.Add(x => x.Count, count).Add(x => x.Label, "items in the list");
            if (max is { } m)
                p.Add(x => x.Max, m);
        });

    [Fact]
    public void Zero_renders_nothing()
    {
        Assert.Empty(Badge(0).Markup.Trim());
    }

    [Theory]
    [InlineData(5, "5")]
    [InlineData(99, "99")]
    [InlineData(120, "99+")]
    public void Counts_and_caps(int count, string shown)
    {
        Assert.Equal(shown, Badge(count).Find(".badge").TextContent.Trim());
    }

    [Fact]
    public void The_cap_is_the_callers_to_set()
    {
        Assert.Equal("9+", Badge(12, max: 9).Find(".badge").TextContent.Trim());
    }

    /// <summary>The name carries the true count, even where the pill shows the cap.</summary>
    [Fact]
    public void Names_its_count_for_a_reader()
    {
        Assert.Equal("5 items in the list", Badge(5).Find(".badge").GetAttribute("aria-label"));
        Assert.Equal("120 items in the list", Badge(120).Find(".badge").GetAttribute("aria-label"));
    }

    [Fact]
    public void One_takes_the_singular_noun()
    {
        var one = RenderComponent<DeskBadge>(p => p.Add(x => x.Count, 1).Add(x => x.Label, "items in the list").Add(x => x.One, "item in the list"));
        Assert.Equal("1 item in the list", one.Find(".badge").GetAttribute("aria-label"));
    }

    [Fact]
    public void Its_figures_are_tabular()
    {
        Assert.Contains("num", Badge(5).Find(".badge").GetAttribute("class"));
    }

    [Fact]
    public void A_one_off_class_and_extra_attributes_join_it()
    {
        var cut = RenderComponent<DeskBadge>(p => p
            .Add(x => x.Count, 3)
            .Add(x => x.Label, "items")
            .Add(x => x.Class, "panel__badge")
            .AddUnmatched("data-testid", "count"));

        var badge = cut.Find(".badge");

        Assert.Contains("panel__badge", badge.GetAttribute("class"));
        Assert.Equal("count", badge.GetAttribute("data-testid"));
    }
}
