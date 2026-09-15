using Microsoft.AspNetCore.HttpLogging;

namespace FileSharing.Api.Extensions;

public static class RequestLoggingExtensions
{
    public static IServiceCollection AddRequestLogging(this IServiceCollection services)
    {
        // Method/path/protocol/scheme/status/duration only (HttpLoggingFields.RequestProperties
        // does NOT include the query string — confirmed against the actual enum values — which
        // matters because SignalR's JWT-over-query-string fallback, ?access_token=..., must never
        // end up in a log line). Explicitly NOT RequestHeaders/ResponseHeaders/RequestBody/
        // ResponseBody — Authorization and Cookie headers must never be logged, and request/
        // response bodies could contain a password, a JWT, or file content.
        // PublicFilesController additionally opts all the way out (see
        // [HttpLogging(HttpLoggingFields.None)] there) because its own RequestPath always
        // contains the public share token.
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        return services;
    }
}
