using FileSharing.Api.Extensions;
using FileSharing.Api.Hubs;
using FileSharing.Api.Middleware;
using FileSharing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthenticationServices(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddCrossCuttingServices(builder.Configuration);

var app = builder.Build();

// Etapa 15: modo de migração controlado para o deployment (uma task ECS one-off, nunca o
// startup normal do container/ECS Service — ver docs/ci-cd.md, seção "Migrations"). Só ativado
// com o argumento de linha de comando "migrate" (`dotnet FileSharing.Api.dll migrate`); qualquer
// outra invocação (inclusive sem argumentos, o caso normal em todo container ECS Service/Docker
// Compose) ignora este bloco e segue o startup normal abaixo, sem nenhuma mudança de
// comportamento. `Database.MigrateAsync()` é idempotente (só aplica migrações ainda não
// registradas em `__EFMigrationsHistory`) — rodar de novo depois de já aplicado é um no-op
// seguro. Duas execuções simultâneas já serializam no próprio Postgres (o migrator do Npgsql
// toma um LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE antes de aplicar qualquer
// migração pendente — verificado empiricamente); como camada adicional, o workflow de deploy
// também nunca dispara duas tasks de migração em paralelo para o mesmo ambiente (ver o
// concurrency do GitHub Actions documentado em docs/ci-cd.md).
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    using var migrationScope = app.Services.CreateScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var migrationLogger = migrationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    migrationLogger.LogInformation("Aplicando migrações pendentes do EF Core (modo 'migrate')...");
    await dbContext.Database.MigrateAsync();
    migrationLogger.LogInformation("Migrações aplicadas com sucesso.");

    return;
}

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

// Etapa 14: prepara a aplicação para rodar atrás de um Application Load Balancer. Sem isto,
// HttpContext.Connection.RemoteIpAddress (usado pelo rate limiting por IP e pelo registro de
// downloads) sempre veria o IP do ALB, nunca o do cliente real, e UseHttpsRedirection abaixo
// nunca saberia que a requisição já chegou como HTTPS no ALB (o tráfego ALB→ECS é HTTP simples).
// Deliberadamente sem restringir KnownProxies/KnownNetworks: o ALB não tem um IP fixo conhecido
// de antemão — a confiança nesses cabeçalhos vem do Security Group (Etapa 14), que garante que
// só o ALB consegue alcançar esta porta, nunca da internet diretamente. Em desenvolvimento local
// (sem proxy nenhum na frente), isto é um no-op: sem cabeçalhos X-Forwarded-*, nada muda.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// ForwardedHeadersOptions defaults KnownIPNetworks/KnownProxies to loopback only, which would
// silently ignore the ALB's headers (a real container-to-container hop, never loopback) — must
// be cleared, not left at their default, for the trust described above to actually take effect.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

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
