namespace FileSharing.Api.Options;

/// <summary>
/// Bound from the "Observability" configuration section. Both flags default to the safest
/// value (off) if the section is absent entirely — a fresh/misconfigured environment fails
/// safe (no request logging, no detailed errors) rather than accidentally verbose or leaky.
/// </summary>
public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// When true, the client-facing ProblemDetails for an unhandled exception includes the
    /// exception's own Message (never its stack trace) in <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails.Detail"/>.
    /// Must be false in every checked-in appsettings.json — this is a local-developer-only,
    /// opt-in convenience (set via user secrets/environment variable), never something a
    /// deployed environment should enable, since even an exception message alone can echo back
    /// infrastructure detail (a SQL fragment, a validation constraint name).
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Toggles the built-in ASP.NET Core HttpLoggingMiddleware (method/path/status/duration
    /// only — see Program.cs for the exact HttpLoggingFields used). Safe to leave true in every
    /// environment; exists as a single, obvious switch to quiet it down (e.g. a very
    /// high-traffic production environment that prefers to rely on an upstream load balancer's
    /// own access logs instead) without touching code.
    /// </summary>
    public bool EnableRequestLogging { get; set; } = true;
}
