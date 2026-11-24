using XLines.Core.Contracts;
using XLines.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace XLines.Data.CrossReference;

/// <summary>
/// How a reader keeps to the tier it was asked for (ADR 0024): it reads the sources the person
/// may see once, then narrows each provider-supplied table to them.
///
/// <para>
/// The answer is the set of sources that <i>may</i> be read, not the set that may not, so a
/// reader that cannot see personal-use data fails closed: a source added later is hidden until
/// someone classifies it <see cref="SourceLicence.Displayable"/>. When everything is visible the
/// answer is null and the query is left exactly as it was, so a local desk sends the same SQL it
/// always has.
/// </para>
/// </summary>
public static class DataTierQueries
{
    /// <summary>The sources this reader may read, or null when it may read all of them.</summary>
    public static async Task<int[]?> VisibleSourceIdsAsync(
        this XLinesDbContext db, IDataTier tier, CancellationToken ct = default)
        => tier.CanSeePersonalUseSources
            ? null
            : await db.Sources.AsNoTracking()
                .Where(s => s.Licence == SourceLicence.Displayable)
                .Select(s => s.Id)
                .ToArrayAsync(ct);

    /// <summary>The rows supplied by one of <paramref name="visible"/>; every row when it is null.</summary>
    public static IQueryable<T> FromVisible<T>(this IQueryable<T> rows, int[]? visible)
        where T : class, IFromSource
        => visible is null ? rows : rows.Where(r => visible.Contains(r.SourceId));
}
