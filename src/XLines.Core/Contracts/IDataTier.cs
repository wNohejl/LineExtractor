namespace XLines.Core.Contracts;

/// <summary>
/// Which tier of the data the person reading may see (ADR 0024).
///
/// <para>
/// Every row a provider supplies carries its source, and every source a licence. A reader asks
/// this once and leaves out what came from a <see cref="Entities.SourceLicence.PersonalUse"/>
/// source when the answer is no, so a desk others can reach shows them only what the providers'
/// terms let it show. The readers ask; the host answers. A host that registers nothing gets
/// <see cref="FullDataTier"/>, so the Worker, the tests and a laptop read everything as before.
/// </para>
/// </summary>
public interface IDataTier
{
    /// <summary>
    /// True when rows from a personal-use source (ESPN, the MLB Stats API, odds-api.io's free
    /// tier) may be read: always on a local desk, and on a shared one only for the operator.
    /// </summary>
    bool CanSeePersonalUseSources { get; }
}

/// <summary>Everything is visible. The default, for a desk with one person at it.</summary>
public sealed class FullDataTier : IDataTier
{
    public static readonly FullDataTier Instance = new();

    public bool CanSeePersonalUseSources => true;
}

/// <summary>
/// Implemented by every row a provider supplied, so a reader can leave out the rows of the
/// sources the person reading may not see.
/// </summary>
public interface IFromSource
{
    int SourceId { get; }
}
