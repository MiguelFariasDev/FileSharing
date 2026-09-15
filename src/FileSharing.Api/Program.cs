using FileSharing.Api.Extensions;
using FileSharing.Api.Hubs;
using FileSharing.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthenticationServices(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddCrossCuttingServices(builder.Configuration);

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
