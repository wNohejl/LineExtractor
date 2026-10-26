using System.Globalization;

namespace LineOps.Web.Tests.Support;

/// <summary>
/// Holds the current culture at en-US for a test that reads dollar amounts, and puts the previous
/// one back on dispose. The desk writes money with "C0" in the machine's culture, and the Linux
/// runner CI uses has none, so "$325" renders there as "¤325".
/// </summary>
public sealed class UsDollars : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentCulture;

    public UsDollars() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _before;
}
