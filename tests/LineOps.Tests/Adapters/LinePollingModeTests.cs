using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Configuration;

namespace LineOps.Tests.Adapters;

/// <summary>
/// Who is allowed to spend credits.
///
/// The board's prices come from a metered feed, and the scheduler used to scan on its own
/// cadence — which on a 500-credit month meant nearly half the allowance went on background
/// scans nobody asked for, overnight and while the desk was empty. Fetching lines is now
/// something an operator does deliberately.
///
/// These pin the default rather than the plumbing, because the failure is silent in the
/// expensive direction: nothing throws when the scheduler starts spending, the credits simply
/// go.
/// </summary>
public class LinePollingModeTests
{
    private static IngestionOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new IngestionOptions();
        configuration.GetSection(IngestionOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void NothingIsFetchedUnaskedByDefault()
    {
        var options = Bind([]);

        // A fresh clone must not spend on a schedule it never agreed to. Unattended spending is
        // something switched on by someone who has read the price.
        Assert.Equal(LinePollingMode.Manual, options.LinePolling.Mode);
        Assert.False(options.LinePolling.RunsUnattended);
    }

    [Fact]
    public void UnattendedScanningHasToBeAskedForByName()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Ingestion:LinePolling:Mode"] = "Scheduled"
        });

        Assert.True(options.LinePolling.RunsUnattended);
    }

    [Fact]
    public void ManualModeStillLeavesTheCadenceMathsIntact()
    {
        var options = Bind([]);

        // Manual stops the scheduler spending; it does not mean the budget is unknown. The Ops
        // panel still reports what a press of Pull lines would draw on, which is computed from
        // exactly these numbers.
        Assert.True(options.LinePolling.CreditsPerSportPerScan > 0);
        Assert.True(options.LinePolling.MonthlyCreditReserve > 0);
    }

    [Fact]
    public void RunOnStartupIsOffSoARestartCostsNothing()
    {
        var options = Bind([]);

        // Belt and braces with the scheduler change: startup now fetches the slate rather than
        // the lines, but a restart loop should not be fetching anything on its own either.
        Assert.False(options.RunOnStartup);
    }
}
