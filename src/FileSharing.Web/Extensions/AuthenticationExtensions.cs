using FileSharing.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FileSharing.Web.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        // [Authorize] on a routed Razor component (Dashboard.razor) attaches authorization
        // metadata to its endpoint, which ASP.NET Core's endpoint routing enforces via
        // AuthorizationMiddleware even without an explicit UseAuthorization() call. For the very
        // first (static, pre-circuit) request to a protected page, that middleware's
        // "not authorized" path calls ChallengeAsync() against the default scheme — with no
        // scheme registered at all this 500s; with none *configured* to redirect anywhere it
        // fails identically. The cookie scheme below is never actually used to establish real
        // identity (this app's own identity check is ApiAuthenticationStateProvider, entirely
        // separate and JWT-driven) — it exists solely so a static request to a protected page
        // gets a real 302 to /login instead of a 500, before any interactive circuit (and this
        // app's own AuthorizeRouteView/RedirectToLogin) even exists. No cookie is ever issued
        // because nothing in this app calls SignInAsync.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.LoginPath = "/login");
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        // AuthTokenProvider/ApiAuthenticationStateProvider are Scoped so each Blazor Server
        // circuit (effectively, each browser tab's session) gets its own JWT/identity — never
        // shared across users, never touching the browser (see AuthTokenProvider remarks).
        services.AddScoped<AuthTokenProvider>();
        services.AddScoped<ApiAuthenticationStateProvider>();
        services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<ApiAuthenticationStateProvider>());

        return services;
    }
}
