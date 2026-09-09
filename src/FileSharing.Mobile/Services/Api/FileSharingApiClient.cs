using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;

namespace FileSharing.Mobile.Services.Api;

/// <summary>
/// Talks to the FileSharing API over plain HTTP/JSON. It never touches AWS S3 directly —
/// the only thing it gets back for an upload is a presigned URL, which the caller PUTs to
/// on its own. No AWS SDK type appears anywhere in this project.
/// </summary>
public class FileSharingApiClient
{
    private readonly HttpClient _httpClient;
    private string? _accessToken;

    public FileSharingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsAuthenticated => _accessToken is not null;

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken);
        _accessToken = result!.AccessToken;
    }

    public async Task<InitiateUploadResponse> InitiateUploadAsync(InitiateUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthenticatedRequest(HttpMethod.Post, "api/files/upload");
        message.Content = JsonContent.Create(request);

        var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<InitiateUploadResponse>(cancellationToken: cancellationToken))!;
    }

    public async Task<CompleteUploadResponse> CompleteUploadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var message = CreateAuthenticatedRequest(HttpMethod.Post, $"api/files/{fileId}/complete");

        var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CompleteUploadResponse>(cancellationToken: cancellationToken))!;
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);

        if (_accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        return request;
    }
}
