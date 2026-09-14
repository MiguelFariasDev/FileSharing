using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public async Task<ApiResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Returns UserResponse (Id, Email), never an AuthResponse/token — registering does not
        // authenticate the caller (see IAuthService.RegisterAsync); the Register page always
        // sends the user to /login afterward rather than assuming they're signed in.
        using var response = await SendAsync(HttpMethod.Post, "api/auth/register", new RegisterRequest(email, password), authenticated: false, cancellationToken);
        var result = await ReadResultAsync<UserResponse>(response, cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.ErrorType!.Value, result.Message!);
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

    private async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            SessionExpired?.Invoke();

        if (!response.IsSuccessStatusCode)
            return ApiResult<T>.Failure(MapErrorType(response.StatusCode), UserFacingMessageFor(response.StatusCode));

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
        HttpStatusCode.TooManyRequests => ApiErrorType.TooManyRequests,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ApiErrorType.ValidationFailed,
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout => ApiErrorType.Network,
        _ => ApiErrorType.ServerError
    };

    // User-facing text only — never the response body (which, for a ProblemDetails/exception
    // page, could contain internal detail: SQL, stack traces, AWS errors, etc.).
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
