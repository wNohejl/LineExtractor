using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Data;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "LineOps";

    public static IServiceCollection AddLineOpsData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No '{ConnectionStringName}' connection string configured. Set " +
                "ConnectionStrings__LineOps (compose does this from .env), or use " +
                "`dotnet user-secrets` for host-side runs.");

        // Blazor Server components outlive a request, so they take a factory and own the
        // context lifetime per operation. Background services and the ingestion pipeline
        // still want a scoped context, so one is resolved from the same factory.
        services.AddDbContextFactory<LineOpsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        services.AddScoped<LineOpsDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<LineOpsDbContext>>().CreateDbContext());

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<CrossReference.MatchupCrossReference>();
        services.AddScoped<CrossReference.BoardService>();
        services.AddScoped<Core.Contracts.IScheduleReader, ScheduleReader>();

        return services;
    }
}
