using System.Collections.Concurrent;
using System.Data.Common;
using XLines.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace XLines.Web.Tests.Support;

/// <summary>
/// A database that is never reached: every connection a window opens is held at a gate that only
/// the read's own cancellation releases. The load tests ask what was read, when, and what was told
/// to stop, and none of those needs a row to come back.
/// </summary>
public static class GatedDatabase
{
    /// <summary>A port nothing listens on, so a read that got past the gate would fail rather than find a database.</summary>
    private const string Nowhere = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";

    /// <summary>Context options whose every connection opening waits at <paramref name="gate"/>.</summary>
    public static DbContextOptions<XLinesDbContext> Options(ConnectionGate gate)
        => new DbContextOptionsBuilder<XLinesDbContext>()
            .UseNpgsql(Nowhere)
            .AddInterceptors(gate)
            .Options;
}

/// <summary>Holds every connection opening until the read's own token is cancelled, and keeps the tokens.</summary>
public sealed class ConnectionGate : DbConnectionInterceptor
{
    public ConcurrentQueue<CancellationToken> Reads { get; } = new();

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Reads.Enqueue(cancellationToken);
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return result;
    }
}

/// <summary>A context factory over gated options, so a window's own contexts wait at the gate too.</summary>
public sealed class GatedDbFactory(DbContextOptions<XLinesDbContext> options) : IDbContextFactory<XLinesDbContext>
{
    public XLinesDbContext CreateDbContext() => new(options);
}
