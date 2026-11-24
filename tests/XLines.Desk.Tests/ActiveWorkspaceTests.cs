using XLines.Desk.Testing;
using XLines.Desk.Windowing;

namespace XLines.Desk.Tests;

/// <summary>
/// The workspace a desk was arranged from: set by applying one, kept while the desk is worked in,
/// cleared when the desk empties or another is applied, written into the layout by name and found
/// again on restore — and absent, without complaint, from a layout written before it existed.
/// </summary>
public class ActiveWorkspaceTests
{
    private static readonly Workspace Start = new("Start", "The one window that opens on its own.", [FakeWindowCatalog.One]);

    private static readonly Workspace Again = new("Again", "", [FakeWindowCatalog.One], Icon: "icon", Tag: "marker");

    private static WindowManager Desk() => new(new FakeWindowCatalog(
        new FakeWindowCatalog().All, [Start, Again], FakeWindowCatalog.One));

    [Fact]
    public void A_workspace_carries_an_icon_and_a_tag_that_default_to_none()
    {
        Assert.Null(Start.Icon);
        Assert.Null(Start.Tag);
        Assert.Equal("icon", Again.Icon);
        Assert.Equal("marker", Again.Tag);
        Assert.Equal("The one window that opens on its own.", Start.Description);
    }

    [Fact]
    public void Applying_a_workspace_makes_it_active_and_another_replaces_it()
    {
        var desk = Desk();
        Assert.Null(desk.ActiveWorkspace);

        Assert.True(desk.ApplyWorkspace(Start));
        Assert.Same(Start, desk.ActiveWorkspace);

        // Working in it keeps it.
        desk.Open(desk.Catalog.Find(FakeWindowCatalog.Two)!, new Dictionary<string, object> { ["Id"] = 1 });
        Assert.Same(Start, desk.ActiveWorkspace);

        desk.ApplyWorkspace(Again);
        Assert.Same(Again, desk.ActiveWorkspace);
    }

    [Fact]
    public void Emptying_the_desk_clears_it_by_any_road()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Start);
        desk.CloseAll();
        Assert.Null(desk.ActiveWorkspace);

        desk.ApplyWorkspace(Start);
        desk.Close(desk.Windows.Single().Id);
        Assert.Null(desk.ActiveWorkspace);
    }

    [Fact]
    public void Stepping_back_puts_the_workspace_back_with_the_row()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Start);
        desk.CloseAll();

        Assert.True(desk.Back());
        Assert.Same(Start, desk.ActiveWorkspace);
    }

    [Fact]
    public void It_round_trips_through_a_saved_layout_by_name()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Again);

        var json = desk.Capture().Serialise();
        Assert.Contains("\"activeWorkspace\":\"Again\"", json);

        var restored = Desk();
        restored.Restore(DeskLayout.Parse(json)!);
        Assert.Same(Again, restored.ActiveWorkspace);
    }

    [Fact]
    public void A_layout_from_before_the_field_loads_with_none()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Start);
        var json = desk.Capture().Serialise().Replace(",\"activeWorkspace\":\"Start\"", "").Replace(",\"activeWorkspaceSaved\":false", "");
        Assert.DoesNotContain("activeWorkspace", json);

        var layout = DeskLayout.Parse(json);
        Assert.NotNull(layout);
        Assert.Null(layout.ActiveWorkspace);

        var restored = Desk();
        restored.Restore(layout);
        Assert.Single(restored.Windows);
        Assert.Null(restored.ActiveWorkspace);
    }

    [Fact]
    public void A_layout_naming_a_workspace_this_desk_no_longer_offers_restores_none()
    {
        var restored = Desk();
        restored.Restore(new DeskLayout
        {
            MaxConcurrentWindows = 3,
            Windows = [new DeskLayoutWindow(FakeWindowCatalog.One, null, 1, false, null)],
            ActiveWorkspace = "Gone",
        });

        Assert.Null(restored.ActiveWorkspace);
    }

    [Fact]
    public void A_saved_workspace_with_an_application_workspace_s_name_restores_as_itself()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Start);
        desk.SaveWorkspace("Start");
        var mine = desk.UserWorkspaces.Single();
        desk.ApplyWorkspace(mine);

        var layout = desk.Capture();
        Assert.True(layout.ActiveWorkspaceSaved);

        var restored = Desk();
        restored.Restore(DeskLayout.Parse(layout.Serialise())!);
        Assert.Same(restored.UserWorkspaces.Single(), restored.ActiveWorkspace);

        // The application's, by the same name, still restores as the application's.
        desk.ApplyWorkspace(Start);
        Assert.False(desk.Capture().ActiveWorkspaceSaved);
    }

    [Fact]
    public void An_empty_desk_writes_no_workspace()
    {
        var desk = Desk();
        desk.ApplyWorkspace(Start);
        desk.CloseAll();

        Assert.Null(desk.Capture().ActiveWorkspace);
    }
}
