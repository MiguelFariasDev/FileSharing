using FileSharing.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileSharing.Api.HealthChecks;

/// <summary>
/// Readiness check for PostgreSQL — a trivial, read-only query (not <c>Database.CanConnectAsync</c>)
/// on purpose: it proves the whole path (connection, auth, and that a real query executes)
/// works identically against a real Npgsql-backed <see cref="IApplicationDbContext"/> and the
/// InMemory provider FileSharing.ApiTests substitutes in tests, where a provider-specific
/// connection facade could behave differently. Reads through the same <see cref="IApplicationDbContext"/>
/// abstraction every Application service already uses — no new persistence dependency.
/// </summary>
public class PostgresHealthCheck : IHealthCheck
{
    private readonly IApplicationDbContext _dbContext;

    public PostgresHealthCheck(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Cheapest possible real query: no rows need to exist, this only needs to prove the
            // database is reachable and can actually execute a statement.
            await _dbContext.Users.Select(u => u.Id).Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // The exception message (which could include a hostname or a driver-level detail)
            // never reaches the HTTP response — MapHealthChecks's default writer only ever
            // renders the aggregate status text, never an individual check's Exception/Data.
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", ex);
        }
    }
}
