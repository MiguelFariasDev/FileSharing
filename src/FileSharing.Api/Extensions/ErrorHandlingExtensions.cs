using FileSharing.Api.Middleware;

namespace FileSharing.Api.Extensions;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            // Applies to every ProblemDetails response written through IProblemDetailsService —
            // not just GlobalExceptionHandler's, but also the ValidationProblem()/NotFound()/
            // Conflict() helpers controllers already call — so a correlation id is available on
            // any error response, not only unhandled exceptions.
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.GetCorrelationId();
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
