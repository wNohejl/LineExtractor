using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Reliability;

public static class ReliabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared reliability layer. Both the web app and the ingestion worker
    /// take this same library, which is why KPIs, alerting and incidents behave identically
    /// wherever they are observed.
    /// </summary>
    public static IServiceCollection AddLineOpsReliability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ReliabilityOptions>(configuration.GetSection(ReliabilityOptions.SectionName));

        services.AddScoped<KpiCalculator>();
        services.AddScoped<AlertEngine>();
        services.AddScoped<IncidentService>();

        return services;
    }

    /// <summary>Adds the periodic evaluator. Kept separate so a process can consume the
    /// reliability data without also being the thing that produces it.</summary>
    public static IServiceCollection AddLineOpsReliabilityEvaluator(this IServiceCollection services)
    {
        services.AddHostedService<ReliabilityEvaluator>();
        return services;
    }
}
