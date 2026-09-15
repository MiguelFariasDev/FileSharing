using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FileSharing.Api.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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
        });

        return services;
    }
}
