using FileSharing.Application.DTOs.Notifications;
using FileSharing.Web.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace FileSharing.Web.Services.Notifications;

/// <summary>
/// Owns the single HubConnection to /hubs/notifications for this circuit — Dashboard.razor (or
/// any other component) only ever subscribes to <see cref="FileDownloaded"/>/<see cref="StateChanged"/>
/// and calls <see cref="StartAsync"/>/<see cref="StopAsync"/>; none of the HubConnection/transport/
/// reconnect machinery lives in a component.
///
/// Uses FileSharing.Application.DTOs.Notifications.FileDownloadedNotification directly — the
/// exact DTO already defined in Etapa 7, not a second Web-local copy of the same contract. The
/// event name string ("FileDownloaded") must match SignalRFileDownloadNotifier.FileDownloadedEvent
/// on the Api side; Web has no project reference to Api (it only ever talks to it over HTTP/
/// SignalR, per this phase's architecture), so this is a small, deliberate, documented
/// duplication of a wire-protocol constant rather than an assembly reference that would violate
/// that boundary.
/// </summary>
public class SignalRNotificationService : IAsyncDisposable
{
    public const string FileDownloadedEventName = "FileDownloaded";

    private readonly ApiSettings _apiSettings;
    private readonly AuthTokenProvider _tokenProvider;
    private readonly ILogger<SignalRNotificationService> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private HubConnection? _connection;

    public NotificationConnectionState State { get; private set; } = NotificationConnectionState.Disconnected;

    public event Action<FileDownloadedNotification>? FileDownloaded;
    public event Action<NotificationConnectionState>? StateChanged;

    public SignalRNotificationService(IOptions<ApiSettings> apiSettings, AuthTokenProvider tokenProvider, ILogger<SignalRNotificationService> logger)
    {
        _apiSettings = apiSettings.Value;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// No-ops if already connected/connecting — guards against a component accidentally
    /// opening a second HubConnection for the same session (e.g. calling this from more than
    /// one place). Requires <see cref="AuthTokenProvider"/> to already hold a token (call this
    /// only after a successful login).
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
                return;

            var connection = new HubConnectionBuilder()
                .WithUrl(_apiSettings.NotificationsHubUrl, options =>
                {
                    // Read on every (re)connect attempt, not captured once — if the token is
                    // ever refreshed in a future phase, a reconnect would already pick up the
                    // new value.
                    options.AccessTokenProvider = () => Task.FromResult(_tokenProvider.AccessToken);
                })
                // Required by this phase: retries immediately, then at 2s/10s/30s, then gives
                // up and settles into Disconnected — never an unbounded manual retry loop.
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
                .Build();

            connection.On<FileDownloadedNotification>(FileDownloadedEventName, notification => FileDownloaded?.Invoke(notification));

            connection.Reconnecting += _ =>
            {
                SetState(NotificationConnectionState.Reconnecting);
                return Task.CompletedTask;
            };

            connection.Reconnected += _ =>
            {
                SetState(NotificationConnectionState.Connected);
                return Task.CompletedTask;
            };

            connection.Closed += ex =>
            {
                if (ex is not null)
                    _logger.LogWarning(ex, "Notification hub connection closed unexpectedly.");

                SetState(NotificationConnectionState.Disconnected);
                return Task.CompletedTask;
            };

            SetState(NotificationConnectionState.Connecting);

            try
            {
                await connection.StartAsync(cancellationToken);
                _connection = connection;
                SetState(NotificationConnectionState.Connected);
            }
            catch (Exception ex)
            {
                // Best-effort: a failed/unavailable notification connection must never break
                // the rest of the dashboard — it only means real-time updates won't arrive
                // until a future reconnect/manual retry.
                _logger.LogWarning(ex, "Failed to connect to the notification hub.");
                await connection.DisposeAsync();
                SetState(NotificationConnectionState.Disconnected);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Called on logout (see MainLayout.razor) so an authenticated SignalR connection never
    /// outlives the session it was authenticated for.
    /// </summary>
    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_connection is null)
                return;

            var connection = _connection;
            _connection = null;

            await connection.StopAsync();
            await connection.DisposeAsync();
            SetState(NotificationConnectionState.Disconnected);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void SetState(NotificationConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
