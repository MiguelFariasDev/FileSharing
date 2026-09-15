using System.Net;
using System.Reflection;
using Bunit;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Web.Components.Shared;
using FileSharing.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class NotificationInboxBridgeTests
{
    private static void RaiseFileDownloaded(SignalRNotificationService service, FileDownloadedNotification notification)
    {
        var field = typeof(SignalRNotificationService)
            .GetField(nameof(SignalRNotificationService.FileDownloaded), BindingFlags.NonPublic | BindingFlags.Instance);

        var handler = (MulticastDelegate?)field?.GetValue(service);
        handler?.DynamicInvoke(notification);
    }

    [Fact]
    public void FileDownloadedEvent_AddsToTheInbox_AndShowsAToast_EvenWithoutAnyPageOpen()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<NotificationInboxBridge>();
        var toastCut = ctx.RenderComponent<ToastContainer>();
        var notificationService = ctx.Services.GetRequiredService<SignalRNotificationService>();
        var inbox = ctx.Services.GetRequiredService<NotificationInboxService>();

        cut.InvokeAsync(() => RaiseFileDownloaded(notificationService, new FileDownloadedNotification(Guid.NewGuid(), "relatorio.pdf", DateTimeOffset.UtcNow)));

        toastCut.WaitForAssertion(() => Assert.Contains("foi baixado", toastCut.Markup));
        Assert.Single(inbox.Entries);
        Assert.Equal("relatorio.pdf", inbox.Entries[0].Notification.OriginalFileName);
    }
}
