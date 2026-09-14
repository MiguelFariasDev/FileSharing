using FileSharing.Api;
using FileSharing.Api.Extensions;
using FileSharing.Api.Hubs;
using FileSharing.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAuthServices(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddBackgroundJobs(builder.Configuration);
builder.Services.AddNotifications();
builder.Services.AddSwaggerWithJwtSupport();
builder.Services.AddObservability(builder.Configuration);

builder.Services.AddProblemDetails(options =>
{
    // Applies to every ProblemDetails response written through IProblemDetailsService — not
    // just GlobalExceptionHandler's, but also the ValidationProblem()/NotFound()/Conflict()
    // helpers controllers already call — so a correlation id is available on any error
    // response, not only unhandled exceptions.
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.GetCorrelationId();
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Method/path/protocol/scheme/status/duration only (HttpLoggingFields.RequestProperties does
// NOT include the query string — confirmed against the actual enum values — which matters
// because SignalR's JWT-over-query-string fallback, ?access_token=..., must never end up in a
// log line). Explicitly NOT RequestHeaders/ResponseHeaders/RequestBody/ResponseBody —
// Authorization and Cookie headers must never be logged, and request/response bodies could
// contain a password, a JWT, or file content. PublicFilesController additionally opts all the
// way out (see [HttpLogging(HttpLoggingFields.None)] there) because its own RequestPath always
// contains the public share token.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Slows down brute-force guessing of public share tokens against GET /api/public/files/{token}.
    // A single shared fixed window (not partitioned per client) is intentional for this phase —
    // it is the simplest limiter that still caps the guess rate; per-IP partitioning can be
    // layered on later without touching the endpoint itself.
    options.AddFixedWindowLimiter(RateLimiterPolicyNames.PublicFiles, limiterOptions =>
    {
        limiterOptions.PermitLimit = 30;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });

    // Slows down credential-stuffing/brute-force attempts against login/register. Partitioned
    // per client IP (unlike the policy above) — one attacker hammering these endpoints is
    // throttled without limiting every other user's ability to log in at the same time.
    // PermitLimit is read from configuration (not hardcoded) per request rather than once at
    // startup so the ApiTests "Testing" environment can override it to an effectively-unlimited
    // value (see CustomWebApplicationFactory) without every other functional test — most of
    // which log in at least once — tripping it; a dedicated test overrides it back down to
    // prove the policy itself works (see AuthRateLimitingTests).
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

var app = builder.Build();

// First in the pipeline, before anything else (including UseExceptionHandler): establishes the
// correlation id and attaches the response header even if a later middleware throws, and opens
// the logging scope every log line for this request should carry.
app.UseMiddleware<CorrelationIdMiddleware>();

// Applied early so every response — including error pages and rejected/rate-limited requests —
// carries these headers. No Content-Security-Policy here: this is a JSON API, and the only HTML
// it ever serves is Swagger UI in Development, which relies on its own inline scripts/styles
// that a CSP would break for no real security benefit in a dev-only, non-production surface.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    await next();
});

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

if (app.Configuration.GetValue("Observability:EnableRequestLogging", true))
{
    app.UseHttpLogging();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Requires authentication like any other endpoint (NotificationHub carries [Authorize]) — no
// anonymous connection is accepted. Not subject to the public-files rate limiting policy,
// which is scoped to PublicFilesController only (see RateLimiterPolicyNames).
app.MapHub<NotificationHub>(HubEndpoints.Notifications);

// No dashboard is mapped here — see BackgroundJobsExtensions. No-ops when Hangfire storage
// was not configured above (disabled via config, or no PostgreSQL connection string present).
app.UseExpiredFileCleanupSchedule();

// Liveness: no dependency on anything external ("self" check only) — an orchestrator restarting
// the process on a liveness failure should only ever do that because the process itself is
// stuck, never because a downstream dependency happens to be slow/down. Readiness: PostgreSQL +
// S3/LocalStack — an orchestrator should stop routing traffic here while either is unreachable,
// without restarting the process. Both use the framework's own default response writer (a plain
// "Healthy"/"Unhealthy" status text) — no custom writer that might otherwise be tempted to
// serialize exception details/connection strings into the response body.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();
