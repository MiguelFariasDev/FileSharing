using System.Threading.RateLimiting;
using FileSharing.Api.Middleware;
using FileSharing.Application.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FileSharing.Api.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Same {status, title, correlationId, code} shape every other error response uses
            // (see ErrorHandlingExtensions.CustomizeProblemDetails) — written directly here
            // instead, since a rejection happens in rate-limiter middleware, upstream of
            // GlobalExceptionHandler/IProblemDetailsService's own pipeline position.
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var correlationId = context.HttpContext.GetCorrelationId();
                context.HttpContext.Response.Headers.TryAdd(CorrelationIdMiddleware.HeaderName, correlationId);

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições em pouco tempo. Aguarde um momento e tente novamente.",
                    Extensions =
                    {
                        ["correlationId"] = correlationId,
                        ["code"] = ErrorCodeCatalog.Map(RateLimitErrorCode.TooManyRequests)
                    }
                };

                await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            };

            // Slows down brute-force guessing of public share tokens against
            // GET /api/public/files/{token}. A single shared fixed window (not partitioned per
            // client) is intentional for this phase — it is the simplest limiter that still caps
            // the guess rate; per-IP partitioning can be layered on later without touching the
            // endpoint itself.
            options.AddFixedWindowLimiter(RateLimiterPolicyNames.PublicFiles, limiterOptions =>
            {
                limiterOptions.PermitLimit = 30;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });

            // Slows down credential-stuffing/brute-force attempts against login/register.
            // Partitioned per client IP (unlike the policy above) — one attacker hammering these
            // endpoints is throttled without limiting every other user's ability to log in at the
            // same time. PermitLimit is read from configuration (not hardcoded) per request
            // rather than once at startup so the ApiTests "Testing" environment can override it to
            // an effectively-unlimited value (see CustomWebApplicationFactory) without every other
            // functional test — most of which log in at least once — tripping it; a dedicated test
            // overrides it back down to prove the policy itself works (see AuthRateLimitingTests).
            options.AddPolicy(RateLimiterPolicyNames.Auth, httpContext =>
            {
                var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
                var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 20);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });

            // Forgot-password specifically: a much tighter, per-IP limit than the general Auth
            // policy above. This is the endpoint that (a) triggers an outbound email for every
            // accepted request and (b) is the one an attacker would hammer to try to enumerate
            // registered accounts by timing/side channel — a small budget (default 5 per 10
            // minutes per IP) is enough for a real user who mistypes or re-requests, while
            // making both spamming the email provider and a sustained enumeration attempt
            // impractical. Same "read from configuration per-request" reasoning as the Auth
            // policy — see its own remarks.
            options.AddPolicy(RateLimiterPolicyNames.PasswordReset, httpContext =>
            {
                var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
                var permitLimit = configuration.GetValue("RateLimiting:PasswordReset:PermitLimit", 5);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }
}
