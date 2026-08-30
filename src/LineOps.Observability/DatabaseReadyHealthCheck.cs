using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LineOps.Observability;

/// <summary>
/// Readiness: can this instance actually serve? Postgres reachable, and the schema at the version
/// this build expects.
///
/// The distinction from liveness is the point. A process that is running but pointed at a database
/// with pending migrations will start, accept traffic, and fail every request — restarting it does
/// not help, so it must read as not-ready rather than not-alive. Reporting the two as one thing is
/// how an orchestrator ends up in a restart loop against a problem no restart can fix.
/// </summary>
public class DatabaseReadyHealthCheck(LineOpsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (pending.Count > 0)
                return HealthCheckResult.Unhealthy(
                    $"Database is reachable but {pending.Count} migration(s) are pending: "
                    + string.Join(", ", pending));

            return HealthCheckResult.Healthy("Database reachable and schema up to date.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is not reachable.", ex);
        }
    }
}
