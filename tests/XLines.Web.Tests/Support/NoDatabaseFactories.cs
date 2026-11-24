using XLines.Data;
using Microsoft.EntityFrameworkCore;

namespace XLines.Web.Tests.Support;

/// <summary>
/// A database that refuses at once: every context asked for throws, so a window's load fails and
/// the test places what it would have read. For windows whose reads go through a seam the test
/// fakes, and that need a factory registered only so they can be built.
/// </summary>
public sealed class UnreachableFactory : IDbContextFactory<XLinesDbContext>
{
    public XLinesDbContext CreateDbContext() => throw new NotSupportedException();

    public Task<XLinesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// A database that never answers: an asynchronous context never arrives, so the load stays in
/// flight, and a synchronous one throws, so a load that asks that way fails, as the panel reads it.
/// </summary>
public sealed class HangingDbFactory : IDbContextFactory<XLinesDbContext>
{
    public XLinesDbContext CreateDbContext() => throw new NotSupportedException();

    public Task<XLinesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => new TaskCompletionSource<XLinesDbContext>().Task;
}
