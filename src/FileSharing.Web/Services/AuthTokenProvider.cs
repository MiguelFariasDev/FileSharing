namespace FileSharing.Web.Services;

/// <summary>
/// Holds the current JWT in server-side memory only, for the lifetime of this Blazor Server
/// circuit — registered Scoped, so each user's browser tab gets its own instance and never
/// sees another's token. Deliberately does NOT use localStorage/sessionStorage/a cookie: since
/// this app already runs entirely server-side (Blazor Server, not WebAssembly), the token never
/// needs to reach the browser or JavaScript at all, which is strictly more secure than any
/// browser-storage option. The trade-off, documented here and in README.md: a hard page reload
/// creates a brand new circuit (and a fresh instance of this class), so the user is signed out
/// on a hard refresh — a transient reconnect (the built-in ReconnectModal, e.g. a brief network
/// blip) reuses the same circuit and does not lose this state.
/// </summary>
public class AuthTokenProvider
{
    public string? AccessToken { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public void SetToken(string accessToken, DateTimeOffset expiresAt)
    {
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
    }

    public void Clear()
    {
        AccessToken = null;
        ExpiresAt = null;
    }
}
