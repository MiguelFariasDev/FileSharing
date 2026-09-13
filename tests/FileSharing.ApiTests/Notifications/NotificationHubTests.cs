using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;

namespace FileSharing.ApiTests.Notifications;

/// <summary>
/// Exercises the real SignalR wire path — NotificationHub, SubClaimUserIdProvider,
/// SignalRFileDownloadNotifier — end to end against the in-memory TestServer, using an actual
/// Microsoft.AspNetCore.SignalR.Client HubConnection rather than a mocked
/// IFileDownloadNotifier (see NotificationHubWebApplicationFactory for why this needs its own
/// factory). LongPolling is forced because TestServer does not support real WebSockets.
/// </summary>
public class NotificationHubTests : IClassFixture<NotificationHubWebApplicationFactory>
{
    private const string Password = "SenhaForte123";
    private const string HubPath = "/hubs/notifications";

    private readonly NotificationHubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NotificationHubTests(NotificationHubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<(Guid UserId, string AccessToken)> RegisterAndLoginAsync()
    {
        var email = UniqueEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var login = (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!;

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await (await _client.SendAsync(meRequest)).Content.ReadFromJsonAsync<UserResponse>();

        return (me!.Id, login.AccessToken);
    }

    private HubConnection BuildConnection(string? jwt)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // TestServer has no real socket to upgrade — LongPolling is the standard,
                // documented way to exercise a SignalR hub against WebApplicationFactory.
                options.Transports = HttpTransportType.LongPolling;

                if (jwt is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(jwt);
            })
            .Build();
    }

    private async Task<(Guid FileId, string PublicToken)> UploadAndLinkFileAsync(string ownerAccessToken)
    {
        HttpRequestMessage Authenticated(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerAccessToken);
            return request;
        }

        using var initiateRequest = Authenticated(HttpMethod.Post, "/api/files/upload");
        initiateRequest.Content = JsonContent.Create(new InitiateUploadRequest("document.pdf", "application/pdf", 1024, false));
        var initiateResponse = await _client.SendAsync(initiateRequest);
        Assert.Equal(HttpStatusCode.Created, initiateResponse.StatusCode);
        var initiated = (await initiateResponse.Content.ReadFromJsonAsync<InitiateUploadResponse>())!;

        _factory.FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageObjectMetadata(1024, "application/pdf"));

        using var completeRequest = Authenticated(HttpMethod.Post, $"/api/files/{initiated.FileId}/complete");
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        using var linkRequest = Authenticated(HttpMethod.Post, $"/api/files/{initiated.FileId}/link");
        var linkResponse = await _client.SendAsync(linkRequest);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>();

        return (initiated.FileId, link.GetProperty("accessToken").GetString()!);
    }

    private static async Task<FileDownloadedNotification> WaitForNotificationAsync(TaskCompletionSource<FileDownloadedNotification> tcs)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == tcs.Task, "Timed out waiting for a FileDownloaded notification.");
        return await tcs.Task;
    }

    [Fact]
    public async Task Connection_WithoutJwt_IsRejected()
    {
        await using var connection = BuildConnection(jwt: null);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Connection_WithValidJwt_IsAccepted()
    {
        var (_, accessToken) = await RegisterAndLoginAsync();
        await using var connection = BuildConnection(accessToken);

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Download_NotifiesOnlyTheOwner_NeverAnUnrelatedUser()
    {
        var (_, ownerAccessToken) = await RegisterAndLoginAsync();
        var (_, bystanderAccessToken) = await RegisterAndLoginAsync();
        var (fileId, publicToken) = await UploadAndLinkFileAsync(ownerAccessToken);

        await using var ownerConnection = BuildConnection(ownerAccessToken);
        await using var bystanderConnection = BuildConnection(bystanderAccessToken);

        var ownerReceived = new TaskCompletionSource<FileDownloadedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bystanderReceivedAnything = false;

        ownerConnection.On<FileDownloadedNotification>(FileSharing.Api.Hubs.SignalRFileDownloadNotifier.FileDownloadedEvent, ownerReceived.SetResult);
        bystanderConnection.On<FileDownloadedNotification>(FileSharing.Api.Hubs.SignalRFileDownloadNotifier.FileDownloadedEvent, _ => bystanderReceivedAnything = true);

        await ownerConnection.StartAsync();
        await bystanderConnection.StartAsync();

        // Downloaded anonymously, exactly like a real public-link recipient would.
        var downloadResponse = await _client.GetAsync($"/api/public/files/{publicToken}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);

        var notification = await WaitForNotificationAsync(ownerReceived);

        Assert.Equal(fileId, notification.FileId);
        Assert.Equal("document.pdf", notification.OriginalFileName);

        // Give the bystander a fair chance to (wrongly) receive something before asserting
        // isolation — SignalR delivery to the owner is asynchronous/best-effort.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(bystanderReceivedAnything, "An unrelated user must never receive another owner's FileDownloaded event.");
    }
}
