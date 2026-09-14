using System.Security.Claims;
using FileSharing.Application.DTOs.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace FileSharing.Web.Services;

/// <summary>
/// The Blazor-facing half of authentication — <see cref="AuthTokenProvider"/> is the source of
/// truth for the JWT itself, this class is the source of truth for the <see cref="ClaimsPrincipal"/>
/// that drives &lt;AuthorizeView&gt;/&lt;AuthorizeRouteView&gt;/[Authorize] page protection.
///
/// Built from GET /api/auth/me's response rather than by decoding the JWT locally — this reuses
/// an endpoint the Api already exposes and tests, instead of adding a JWT-parsing dependency
/// here just to read the same two claims.
/// </summary>
public class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private ClaimsPrincipal _currentUser = Anonymous;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    public void MarkUserAsAuthenticated(UserResponse user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Email)
            ],
            authenticationType: "Api");

        _currentUser = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void MarkUserAsLoggedOut()
    {
        _currentUser = Anonymous;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
