using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LineOps.Data;

/// <summary>
/// Used only by `dotnet ef` at design time so migrations can be created without
/// booting the web host. Runtime connection strings come from configuration.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LineOpsDbContext>
{
    public LineOpsDbContext CreateDbContext(string[] args)
    {
        // No embedded credential: `dotnet ef` runs against whatever LINEOPS_CONNECTION
        // points at, and Npgsql picks up the standard PGPASSWORD if the password is
        // supplied that way instead.
        var connectionString = Environment.GetEnvironmentVariable("LINEOPS_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=lineops;Username=lineops";

        var options = new DbContextOptionsBuilder<LineOpsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new LineOpsDbContext(options);
    }
}
