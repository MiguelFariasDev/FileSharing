using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.Authentication;

namespace FileSharing.Mobile.Core.Services.ApiClient;

/// <summary>
/// The only type in this project that calls FileSharing.Api — mirrors the role
/// FileSharing.Web.Services.FileSharingApiClient plays there (single place for Authorization
/// handling, 401 detection, and HTTP-status-to-ApiErrorType mapping), adapted for a client that
/// reads its token from a persisted AuthSession rather than a per-circuit in-memory field.
/// Never logs anything — no ILogger dependency at all — so there is no code path here that
/// could ever accidentally log the Authorization header, the JWT, an AccessToken, or a
/// presigned URL (see docs/mobile.md's security section).
/// </summary>
public class FileSharingApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthSession _authSession;

    /// <summary>
    /// Raised whenever an authenticated call comes back 401 — the session's token is no longer
    /// valid. A single subscriber (App/Shell startup logic) reacts by clearing the local session
    /// and navigating to the login page.
    /// </summary>
    public event Action? SessionExpired;

    public FileSharingApiClient(HttpClient httpClient, AuthSession authSession)
    {
        _httpClient = httpClient;
        _authSession = authSession;
    }

    public async Task<ApiResult<UserResponse>> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/register", new RegisterRequest(email, password), authenticated: false, cancellationToken);
        return await ReadResultAsync<UserResponse>(response, cancellationToken);
    }

    public async Task<ApiResult<AuthResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/login", new LoginRequest(email, password), authenticated: false, cancellationToken);
        return await ReadResultAsync<AuthResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Always success from this client's point of view for any 202 — the Api's own response is
    /// deliberately identical whether or not the email belongs to an account (see
    /// AuthController.ForgotPassword), so there is nothing more specific for this client to
    /// expose either. Never reveal to the user whether the email exists.
    /// </summary>
    public async Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/forgot-password", new ForgotPasswordRequest(email), authenticated: false, cancellationToken);
        var result = await ReadResultAsync<object>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!, result.Code);
    }

    /// <summary>Lets the reset-password screen check a token before showing the "new password" form — see AuthController.ValidateResetToken.</summary>
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

    public async Task<ApiResult<PublicLinkResponse>> GenerateLinkAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/files/{fileId}/link", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<PublicLinkResponse>(response, cancellationToken);
    }

    public async Task<ApiResult<InitiateUploadResponse>> InitiateUploadAsync(InitiateUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/files/upload", request, authenticated: true, cancellationToken);
        return await ReadResultAsync<InitiateUploadResponse>(response, cancellationToken);
    }

    public async Task<ApiResult<CompleteUploadResponse>> CompleteUploadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/files/{fileId}/complete", body: null, authenticated: true, cancellationToken);
        return await ReadResultAsync<CompleteUploadResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (authenticated && _authSession.AccessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a caller-requested cancellation.
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        }
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

    // User-facing text only — never the raw response body, which for a ProblemDetails/exception
    // page could contain internal detail (SQL, stack traces, AWS errors). In practice the Api
    // always includes its own safe "title" in the body, which ReadResultAsync prefers over this —
    // these are the fallback for a response with no parseable body at all.
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

    private static readonly JsonSerializerOptions ErrorBodyJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Every failure response from this Api is a ProblemDetails-shaped body — {"title": ...,
    /// "code": ..., "correlationId": ...} — see GlobalExceptionHandler/ErrorHandlingExtensions
    /// (FileSharing.Api). Returns (null, null) for a body that isn't there at all (a
    /// network-level synthetic response) or doesn't parse as JSON.
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
}
