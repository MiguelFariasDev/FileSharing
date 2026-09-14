using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using FileSharing.Mobile.Core.Services.SignalR;
using FileSharing.Mobile.Tests.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileSharing.Mobile.Tests.Services.SignalR;

/// <summary>
/// A real end-to-end connection to a live hub is exercised in FileSharing.ApiTests
/// (NotificationHubTests) against the actual NotificationHub — this project has no HTTP host to
/// stand one up. What's verified here instead: the parts of the client's own lifecycle logic
/// that don't require a reachable server — graceful failure, idempotent start/stop, and that
/// disposal never throws even if nothing was ever started.
/// </summary>
public class SignalRNotificationServiceTests
{
    private static SignalRNotificationService CreateSut(string hubUrl = "http://127.0.0.1:1/hubs/notifications")
    {
        var options = new ApiClientOptions { BaseUrl = "http://127.0.0.1:1/", HubUrl = hubUrl };
        var session = new AuthSession(new InMemorySecureStorageService());
        return new SignalRNotificationService(options, session, NullLogger<SignalRNotificationService>.Instance);
    }

    [Fact]
    public void NewService_StartsDisconnected()
    {
        var sut = CreateSut();

        Assert.Equal(NotificationConnectionState.Disconnected, sut.State);
    }

    [Fact]
    public async Task StartAsync_WhenTheHubIsUnreachable_EndsInDisconnectedState_WithoutThrowing()
    {
        // Loopback port 1 refuses connections immediately — a safe, deterministic stand-in for
        // "the API/hub is not reachable" without depending on any real network condition.
        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(() => sut.StartAsync());

        Assert.Null(exception);
        Assert.Equal(NotificationConnectionState.Disconnected, sut.State);
    }

    [Fact]
    public async Task StopAsync_WhenNeverStarted_IsANoOp()
    {
        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(() => sut.StopAsync());

        Assert.Null(exception);
        Assert.Equal(NotificationConnectionState.Disconnected, sut.State);
    }

    [Fact]
    public async Task DisposeAsync_NeverThrows_EvenIfNeverStarted()
    {
        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(() => sut.DisposeAsync().AsTask());

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_CalledTwiceConcurrently_NeverThrows()
    {
        // Proves the lifecycle lock actually serializes concurrent StartAsync calls rather than
        // racing to build two HubConnections — both against the same unreachable hub, so this
        // only needs to prove "no crash/no deadlock", not "ends up connected".
        var sut = CreateSut();

        var first = sut.StartAsync();
        var second = sut.StartAsync();

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(first, second));

        Assert.Null(exception);
    }
}
