using FileSharing.Application.DTOs.Notifications;
using FileSharing.Mobile.Core.Services.Activity;
using FileSharing.Mobile.Core.ViewModels;
using FileSharing.Mobile.Tests.Fakes;
using FileSharing.Mobile.Tests.Services;

namespace FileSharing.Mobile.Tests.ViewModels;

public class ActivityViewModelTests
{
    private static (ActivityViewModel ViewModel, FakeNotificationService Notifications) CreateSut()
    {
        var notifications = new FakeNotificationService();
        var dispatcher = new ImmediateMainThreadDispatcher();
        var feed = new ActivityFeedService(notifications, dispatcher);
        return (new ActivityViewModel(feed), notifications);
    }

    [Fact]
    public void NoEvents_IsEmpty()
    {
        var (sut, _) = CreateSut();

        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void FileDownloadedEvent_AddsANewestFirstEntry()
    {
        var (sut, notifications) = CreateSut();

        notifications.RaiseFileDownloaded(new FileDownloadedNotification(Guid.NewGuid(), "first.pdf", DateTimeOffset.UtcNow));
        notifications.RaiseFileDownloaded(new FileDownloadedNotification(Guid.NewGuid(), "second.pdf", DateTimeOffset.UtcNow));

        Assert.False(sut.IsEmpty);
        Assert.Equal(2, sut.Entries.Count);
        Assert.Equal("second.pdf", sut.Entries[0].Notification.OriginalFileName);
        Assert.Equal("first.pdf", sut.Entries[1].Notification.OriginalFileName);
    }
}
