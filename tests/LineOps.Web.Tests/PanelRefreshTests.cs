using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// A panel refreshes itself when the data it names changes, once per burst, and not for data
/// it does not draw from. Before this, every window showed the data as of the last click.
/// </summary>
public class PanelRefreshTests : DeskTestContext
{
    /// <summary>A panel that watches one topic and counts its reloads.</summary>
    private sealed class CountingPanel : PanelBase
    {
        public int Refreshes;

        protected override IReadOnlySet<string> Watches { get; } = new HashSet<string> { "games" };

        protected override Task RefreshAsync()
        {
            Interlocked.Increment(ref Refreshes);
            return Task.CompletedTask;
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
            => builder.AddContent(0, $"refreshed {Refreshes}");
    }

    private DeskSignals Signals => Services.GetRequiredService<DeskSignals>();

    [Fact]
    public void A_burst_of_changes_is_one_refresh()
    {
        var panel = RenderComponent<CountingPanel>();

        Signals.Publish(new HashSet<string> { "games" });
        Signals.Publish(new HashSet<string> { "games", "runs" });
        Signals.Publish(new HashSet<string> { "games" });

        panel.WaitForAssertion(() => Assert.Equal("refreshed 1", panel.Markup), TimeSpan.FromSeconds(5));

        // And nothing further arrives once the burst has settled.
        Thread.Sleep(1500);
        Assert.Equal(1, panel.Instance.Refreshes);
    }

    [Fact]
    public void A_change_to_data_the_panel_does_not_draw_from_is_ignored()
    {
        var panel = RenderComponent<CountingPanel>();

        Signals.Publish(new HashSet<string> { "odds", "alerts" });

        Thread.Sleep(1500);
        Assert.Equal(0, panel.Instance.Refreshes);
    }

    [Fact]
    public void A_closed_panel_stops_listening()
    {
        var panel = RenderComponent<CountingPanel>();
        var instance = panel.Instance;

        DisposeComponents();
        Signals.Publish(new HashSet<string> { "games" });

        Thread.Sleep(1500);
        Assert.Equal(0, instance.Refreshes);
    }
}
