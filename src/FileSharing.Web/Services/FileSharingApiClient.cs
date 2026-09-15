using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Models;

namespace FileSharing.Web.Services;

/// <summary>
/// The only type in this project that calls FileSharing.Api — every component/page goes
/// through here instead of holding its own HttpClient, so Authorization handling, 401
/// detection and status-code-to-ApiErrorType mapping exist in exactly one place.
/// </summary>
public class FileSharingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthTokenProvider _tokenProvider;

    /// <summary>
    /// Raised whenever an authenticated call comes back 401 — the session's token is no longer
    /// valid (expired, or the Api considers it invalid). A single subscriber (see
    /// Components/Shared/SessionGuard.razor, wired into MainLayout) reacts by clearing local
    /// auth state and redirecting to /login; components making the call don't each need to
    /// know how to do that themselves.
    /// </summary>
    public event Action? SessionExpired;

    public FileSharingApiClient(HttpClient httpClient, AuthTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    public async Task<ApiResult<AuthResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/login", new LoginRequest(email, password), authenticated: false, cancellationToken);
        return await ReadResultAsync<AuthResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Always ApiResult.Success() from this client's point of view for any response the Api
    /// itself considers a success (202) — the Api's own response body is deliberately identical
    /// whether or not the email belongs to an account (see AuthController.ForgotPassword), so
    /// there is nothing more specific for this client to expose either.
    /// </summary>
    public async Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/forgot-password", new ForgotPasswordRequest(email), authenticated: false, cancellationToken);
        var result = await ReadResultAsync<object>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!, result.Code);
    }

    /// <summary>Lets the reset-password page check a token before showing the "new password" form — see AuthController.ValidateResetToken.</summary>
    public async Task<ApiResult> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/auth/reset-password/{Uri.EscapeDataString(token)}", body: null, authenticated: false, cancellationToken);
        var result = await ReadResultAsync<object>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!, result.Code);
    }

    public async Task<ApiResult> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/reset-password", new ResetPasswordRequest(token, newPassword), authenticated: false, cancellationToken);
        var result = await ReadResultAsync<object>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!, result.Code);
    }

    public async Task<ApiResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Returns UserResponse (Id, Email), never an AuthResponse/token — registering does not
        // authenticate the caller (see IAuthService.RegisterAsync); the Register page always
        // sends the user to /login afterward rather than assuming they're signed in.
        using var response = await SendAsync(HttpMethod.Post, "api/auth/register", new RegisterRequest(email, password), authenticated: false, cancellationToken);
        var result = await ReadResultAsync<UserResponse>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!, result.Code);
    }

    public async Task<ApiResult<UserResponse>> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/auth/me", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<UserResponse>(response, cancellationToken);
    }

    public async Task<ApiResult<IReadOnlyList<FileSummaryResponse>>> GetMyFilesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/files/mine", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<IReadOnlyList<FileSummaryResponse>>(response, cancellationToken);
    }

    public async Task<ApiResult<IReadOnlyList<DownloadHistoryEntryResponse>>> GetDownloadHistoryAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/files/{fileId}/downloads", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<IReadOnlyList<DownloadHistoryEntryResponse>>(response, cancellationToken);
    }

    /// <summary>
    /// Calls the existing POST /api/files/{id}/link — the Web never generates or constructs a
    /// token itself, it only ever displays whatever the Api returns from this call. Also used
    /// for "regenerate": the endpoint's behavior (overwrite the previous hash) is unchanged
    /// from Etapa 4, so a first-time generation and a regeneration are the exact same call.
    /// </summary>
    public async Task<ApiResult<PublicLinkResponse>> GenerateLinkAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/files/{fileId}/link", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<PublicLinkResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        // Only ever attached for calls the Api requires a JWT for — public/anonymous endpoints
        // (login, register) never carry an Authorization header.
        if (authenticated && _tokenProvider.AccessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return BuildSyntheticResponse(HttpStatusCode.ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a caller-requested cancellation.
            return BuildSyntheticResponse(HttpStatusCode.RequestTimeout);
        }
    }

    private static HttpResponseMessage BuildSyntheticResponse(HttpStatusCode statusCode) => new(statusCode);

    private static readonly JsonSerializerOptions ErrorBodyJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Every failure response from this Api is a ProblemDetails-shaped body — {"title": ...,
    /// "code": ..., "correlationId": ...} — see GlobalExceptionHandler/ErrorHandlingExtensions.
    /// Returns (null, null) for a body that isn't there at all (a network-level synthetic
    /// response) or doesn't parse as JSON — callers fall back to UserFacingMessageFor in that
    /// case, never surfacing a parse failure to the user.
    /// </summary>
    private static async Task<(string? Code, string? Title)> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is 0 or null)
            return (null, null);

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponseBody>(ErrorBodyJsonOptions, cancellationToken);
            return (body?.Code, body?.Title);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private sealed class ErrorResponseBody
    {
        public string? Title { get; set; }
        public string? Code { get; set; }
    }

    private async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            SessionExpired?.Invoke();

        if (!response.IsSuccessStatusCode)
        {
            var errorType = MapErrorType(response.StatusCode);
            var (code, title) = await TryReadErrorBodyAsync(response, cancellationToken);

            // Prefer the Api's own message (already safe, user-facing Portuguese text — see
            // GlobalExceptionHandler) over this client's generic per-status fallback whenever the
            // body actually parsed; a network-level synthetic response (no body at all) is the
            // only case that ever falls back to UserFacingMessageFor.
            return ApiResult<T>.Failure(errorType, title ?? UserFacingMessageFor(response.StatusCode), code);
        }

        // 204/empty body (not currently used by any endpoint this client calls, but a safe
        // default) — treat as success with a default value rather than a deserialization error.
        if (response.Content.Headers.ContentLength is 0)
            return ApiResult<T>.Success(default!);

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return ApiResult<T>.Success(value!);
        }
        catch (System.Text.Json.JsonException)
        {
            return ApiResult<T>.Failure(ApiErrorType.ServerError, "A API retornou uma resposta inesperada.");
        }
    }

    private static ApiErrorType MapErrorType(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => ApiErrorType.Unauthorized,
        HttpStatusCode.Forbidden => ApiErrorType.Forbidden,
        HttpStatusCode.NotFound => ApiErrorType.NotFound,
        HttpStatusCode.Conflict => ApiErrorType.Conflict,
        HttpStatusCode.Gone => ApiErrorType.Gone,
        HttpStatusCode.TooManyRequests => ApiErrorType.TooManyRequests,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ApiErrorType.ValidationFailed,
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout => ApiErrorType.Network,
        _ => ApiErrorType.ServerError
    };

    // User-facing text only — never the response body (which, for a ProblemDetails/exception
    // page, could contain internal detail: SQL, stack traces, AWS errors, etc.). In practice this
    // Api always includes its own safe "title" in the body, which ReadResultAsync prefers over
    // this — these are the fallback for a response with no parseable body at all.
    private static string UserFacingMessageFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Sessão inválida ou expirada. Faça login novamente.",
        HttpStatusCode.Forbidden => "Você não tem permissão para acessar este recurso.",
        HttpStatusCode.NotFound => "Recurso não encontrado.",
        HttpStatusCode.Conflict => "Esta operação não pode ser concluída no estado atual.",
        HttpStatusCode.Gone => "Este link não é mais válido.",
        HttpStatusCode.TooManyRequests => "Muitas tentativas em pouco tempo. Aguarde um momento e tente novamente.",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Dados inválidos.",
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout => "Não foi possível conectar à API. Verifique sua conexão e tente novamente.",
        _ => "Ocorreu um erro inesperado. Tente novamente mais tarde."
    };
}
