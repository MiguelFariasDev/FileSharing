using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.ApiClient;
using FileSharing.Mobile.Core.Services.Authentication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace FileSharing.Mobile.Core.Services.SignalR;

/// <summary>
/// Mirrors FileSharing.Web's own SignalRNotificationService (Fase 7/8) closely — same
/// WithAutomaticReconnect backoff, same idempotent StartAsync guarded by a lock, same
/// State/StateChanged pattern — adapted only in how the JWT is obtained (AuthSession here,
/// AuthTokenProvider there) and lifetime (this app's DI container is process-lifetime, so a
/// single instance is reused across the whole app session rather than per-Blazor-circuit).
///
/// AccessTokenProvider reads AuthSession.AccessToken fresh on every (re)connect — never a
/// captured value from construction time — so a token obtained after this service was built
/// (i.e. every real case, since login always happens after DI wiring) is still picked up
/// correctly. Never logs the token, the hub URL's query string, or the notification payload
/// beyond what FileDownloadedNotification itself already exposes (FileId/OriginalFileName/
/// DownloadedAt — never AccessToken/hash/presigned URL/IP, matching the DTO's own contract).
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly ApiClientOptions _options;
    private readonly AuthSession _authSession;
    private readonly ILogger<SignalRNotificationService> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private HubConnection? _connection;

    public event Action<FileDownloadedNotification>? FileDownloaded;
    public event Action? StateChanged;

    public NotificationConnectionState State { get; private set; } = NotificationConnectionState.Disconnected;

    public SignalRNotificationService(ApiClientOptions options, AuthSession authSession, ILogger<SignalRNotificationService> logger)
    {
        _options = options;
        _authSession = authSession;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            // Idempotent: a caller that already has a connection (or one currently
            // connecting/reconnecting) gets a no-op rather than a second, duplicate connection.
            if (_connection is not null)
                return;

            SetState(NotificationConnectionState.Connecting);

            var connection = new HubConnectionBuilder()
                .WithUrl(_options.HubUrl, httpOptions =>
                {
                    // Read fresh on every (re)connect, never captured once — the token can
                    // legitimately change (login/logout/re-login) across the app's lifetime.
                    httpOptions.AccessTokenProvider = () => Task.FromResult(_authSession.AccessToken);
                })
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
                .Build();

            connection.On<FileDownloadedNotification>("FileDownloaded", notification => FileDownloaded?.Invoke(notification));

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
                SetState(NotificationConnectionState.Disconnected);
                if (ex is not null)
                    _logger.LogWarning(ex, "Notification hub connection closed unexpectedly.");
                return Task.CompletedTask;
            };

            try
            {
                await connection.StartAsync();
                _connection = connection;
                SetState(NotificationConnectionState.Connected);
            }
            catch (Exception ex)
            {
                // Best-effort: a user should still be able to use the rest of the app (see
                // Home/its files) even when real-time notifications are unavailable — e.g. the
                // API is briefly unreachable at app startup.
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

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_connection is null)
                return;

            await _connection.DisposeAsync();
            _connection = null;
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
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleLock.Dispose();
    }
}
