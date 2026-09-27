using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// One control, one definition (spec §7). The selection strip and the stake row were drawn three
/// times — the wager window, the Journal's entry form, the Journal's parlay form — and drifted.
/// Read from the source, the way GridConventionTests enforces the grid's rules.
/// </summary>
public class SelectionGuardTests
{
    [Theory]
    [InlineData("SelectionChips")]
    [InlineData("StakeRow")]
    public void Each_shared_control_has_one_definition(string name)
    {
        var files = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src"), name + ".razor", SearchOption.AllDirectories).ToList();

        var only = Assert.Single(files);
        Assert.EndsWith(Path.Combine("LineOps.Web", "Components", "Desk", name + ".razor"), only);
    }

    [Fact]
    public void No_panel_declares_its_own_stake_field()
    {
        var panels = Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels");

        var offenders = Directory.EnumerateFiles(panels, "*.razor")
            .Where(f => File.ReadAllText(f).Contains("placeholder=\"Stake\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Declare the stake with <StakeRow>, not a field of its own: " + string.Join(", ", offenders));
    }
}
