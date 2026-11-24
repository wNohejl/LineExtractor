using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XLines.Data;

/// <summary>
/// Used only by `dotnet ef` at design time so migrations can be created without
/// booting the web host. Runtime connection strings come from configuration.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<XLinesDbContext>
{
    public XLinesDbContext CreateDbContext(string[] args)
    {
        // No embedded credential: `dotnet ef` runs against whatever XLINES_CONNECTION
        // points at, and Npgsql picks up the standard PGPASSWORD if the password is
        // supplied that way instead.
        var connectionString = Environment.GetEnvironmentVariable("XLINES_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=lineops;Username=lineops";

        var options = new DbContextOptionsBuilder<XLinesDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new XLinesDbContext(options);
    }
}
