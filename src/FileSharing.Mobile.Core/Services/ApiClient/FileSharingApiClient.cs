using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
            return ApiResult<T>.Failure(MapErrorType(response.StatusCode), UserFacingMessageFor(response.StatusCode));

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
        HttpStatusCode.TooManyRequests => ApiErrorType.TooManyRequests,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ApiErrorType.ValidationFailed,
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout => ApiErrorType.Network,
        _ => ApiErrorType.ServerError
    };

    // User-facing text only — never the raw response body, which for a ProblemDetails/exception
    // page could contain internal detail (SQL, stack traces, AWS errors).
    private static string UserFacingMessageFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Sessão inválida ou expirada. Faça login novamente.",
        HttpStatusCode.Forbidden => "Você não tem permissão para acessar este recurso.",
        HttpStatusCode.NotFound => "Recurso não encontrado.",
        HttpStatusCode.Conflict => "Esta operação não pode ser concluída no estado atual.",
        HttpStatusCode.TooManyRequests => "Muitas tentativas em pouco tempo. Aguarde um momento e tente novamente.",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Dados inválidos.",
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout => "Não foi possível conectar à API. Verifique sua conexão e tente novamente.",
        _ => "Ocorreu um erro inesperado. Tente novamente mais tarde."
    };
}
