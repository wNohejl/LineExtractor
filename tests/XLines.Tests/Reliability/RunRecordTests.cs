using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Ingestion;
using XLines.Ingestion.Services;

namespace XLines.Tests.Reliability;

/// <summary>
/// The shared pieces of a run row: provider text cut to its column without splitting a surrogate
/// pair, a fetch's cost copied onto the run, and an exception worded as the run records it.
/// </summary>
public class RunRecordTests
{
    [Fact]
    public void AFitThatWouldSplitASurrogatePairGivesUpOneCharacterInstead()
    {
        // "ab" then U+1F600, two UTF-16 units: a cut at three would keep only its high half.
        var value = "ab\U0001F600cd";

        var fitted = ColumnText.Fit(value, 3);

        Assert.Equal("ab", fitted);
        Assert.False(char.IsHighSurrogate(fitted![^1]));
    }

    [Fact]
    public void AFitAfterACompletePairKeepsThePair()
        => Assert.Equal("ab\U0001F600", ColumnText.Fit("ab\U0001F600cd", 4));

    [Fact]
    public void AFitOfNullIsNull() => Assert.Null(ColumnText.Fit(null, 8));

    [Fact]
    public void AValueShorterThanItsColumnIsUnchanged()
        => Assert.Equal("Out", ColumnText.Fit("Out", 32));

    [Fact]
    public void AValueExactlyItsColumnsLengthIsUnchanged()
        => Assert.Equal("Questionable", ColumnText.Fit("Questionable", "Questionable".Length));

    [Fact]
    public void APlainLongValueIsCutToItsColumn()
        => Assert.Equal("Injured", ColumnText.Fit("Injured Reserve", 7));

    [Fact]
    public void AChargeSetsRequestsCreditsAndTheProvidersRemainder()
    {
        var run = new IngestionRun { RequestsMade = 9, CreditsSpent = 9, ProviderCreditsRemaining = 9 };

        run.Charge(new FetchCost(1, 2, 498));

        Assert.Equal((1, 2, (int?)498), (run.RequestsMade, run.CreditsSpent, run.ProviderCreditsRemaining));
    }

    [Fact]
    public void AChargeWithNoRemainderRecordsNone()
    {
        // ESPN never states a remainder; its runs keep null, as they did before the helper.
        var run = new IngestionRun { ProviderCreditsRemaining = 120 };

        run.Charge(new FetchCost(3));

        Assert.Equal((3, 0, (int?)null), (run.RequestsMade, run.CreditsSpent, run.ProviderCreditsRemaining));
    }

    [Fact]
    public void AnExceptionIsDescribedByItsTypeThenItsMessage()
        => Assert.Equal("HttpRequestException: 503 Service Unavailable",
            RunRecord.Describe(new HttpRequestException("503 Service Unavailable")));
}
