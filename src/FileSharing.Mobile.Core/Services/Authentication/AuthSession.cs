using FileSharing.Application.DTOs.Auth;
using FileSharing.Mobile.Core.Services.Storage;

namespace FileSharing.Mobile.Core.Services.Authentication;

/// <summary>
/// The single source of truth for "is this app currently signed in, as whom, with what token" —
/// mirrors the role FileSharing.Web's ApiAuthenticationStateProvider/AuthTokenProvider play
/// there, adapted for a client that must survive an app restart (SecureStorage-backed) instead
/// of living only for a Blazor Server circuit. FileSharingApiClient reads the current token from
/// here on every authenticated call; it never manages its own token field.
///
/// Persists exactly three values, all through ISecureStorageService (Android Keystore-backed),
/// nothing else: the JWT, its ExpiresAt (needed to decide the session is stale without parsing
/// the token itself — this app has no reason to ever decode a JWT), and the user's email (pure
/// display convenience, not a credential). Never AWS credentials, never a signing secret — this
/// app is never issued either.
/// </summary>
public class AuthSession
{
    private const string TokenKey = "auth.access_token";
    private const string ExpiresAtKey = "auth.expires_at";
    private const string UserIdKey = "auth.user_id";
    private const string EmailKey = "auth.email";

    private readonly ISecureStorageService _secureStorage;

    public event Action? AuthenticationStateChanged;

    public string? AccessToken { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public UserResponse? CurrentUser { get; private set; }

    /// <summary>
    /// True only when a token is present AND its own declared ExpiresAt has not yet passed —
    /// an app reopened long after the last login must not present itself as authenticated just
    /// because SecureStorage still has a (by-now-useless) token sitting in it. The API is still
    /// the final authority (a call can still come back 401 for other reasons — see
    /// FileSharingApiClient.SessionExpired), this is only a same-device, no-network pre-check.
    /// </summary>
    public bool IsAuthenticated => AccessToken is not null && ExpiresAt is { } expiresAt && expiresAt > DateTimeOffset.UtcNow;

    public AuthSession(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
    }

    /// <summary>
    /// Restores a previous session from SecureStorage, if any — called once at app startup.
    /// Never throws: a corrupted/partial stored session is treated the same as no session.
    /// </summary>
    public async Task RestoreAsync()
    {
        try
        {
            var token = await _secureStorage.GetAsync(TokenKey);
            var expiresAtRaw = await _secureStorage.GetAsync(ExpiresAtKey);
            var userIdRaw = await _secureStorage.GetAsync(UserIdKey);
            var email = await _secureStorage.GetAsync(EmailKey);

            if (string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(expiresAtRaw) ||
                !DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt) ||
                string.IsNullOrWhiteSpace(userIdRaw) ||
                !Guid.TryParse(userIdRaw, out var userId) ||
                string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            AccessToken = token;
            ExpiresAt = expiresAt;
            CurrentUser = new UserResponse(userId, email);
        }
        catch
        {
            // Treated as "no session to restore" — never crash app startup over a storage read.
            AccessToken = null;
            ExpiresAt = null;
            CurrentUser = null;
        }
        finally
        {
            AuthenticationStateChanged?.Invoke();
        }
    }

    public async Task SetSessionAsync(AuthResponse auth, UserResponse user)
    {
        AccessToken = auth.AccessToken;
        ExpiresAt = auth.ExpiresAt;
        CurrentUser = user;

        await _secureStorage.SetAsync(TokenKey, auth.AccessToken);
        await _secureStorage.SetAsync(ExpiresAtKey, auth.ExpiresAt.ToString("O"));
        await _secureStorage.SetAsync(UserIdKey, user.Id.ToString());
        await _secureStorage.SetAsync(EmailKey, user.Email);

        AuthenticationStateChanged?.Invoke();
    }

    public void ClearSession()
    {
        AccessToken = null;
        ExpiresAt = null;
        CurrentUser = null;

        _secureStorage.Remove(TokenKey);
        _secureStorage.Remove(ExpiresAtKey);
        _secureStorage.Remove(UserIdKey);
        _secureStorage.Remove(EmailKey);

        AuthenticationStateChanged?.Invoke();
    }
}
