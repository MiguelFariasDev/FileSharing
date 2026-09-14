using FileSharing.Api.HealthChecks;
using FileSharing.Api.Options;
using FileSharing.Application.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileSharing.Api.Extensions;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ObservabilityOptions>(configuration.GetSection(ObservabilityOptions.SectionName));

        // Metrics live for the whole process — a new AppMetrics per request/scope would create
        // a fresh, disconnected Meter each time instead of one continuously-observable series.
        services.AddSingleton<AppMetrics>();

        services.AddHealthChecks()
            // Liveness: "is the process up and able to respond at all" — intentionally has no
            // dependency on anything external, so it can never report unhealthy just because
            // PostgreSQL/S3 happen to be down (that is what readiness is for).
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<S3HealthCheck>("s3", tags: ["ready"]);

        return services;
    }
}
