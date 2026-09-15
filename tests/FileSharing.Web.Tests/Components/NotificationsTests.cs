using System.Net;
using Bunit;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Web.Components.Pages;
using FileSharing.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class NotificationsTests
{
    [Fact]
    public void NoNotificationsYet_ShowsTheEmptyState()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Notifications>();

        Assert.Contains("Nenhuma notificação ainda", cut.Markup);
    }

    [Fact]
    public void ExistingEntries_AreListed_NewestFirst_LinkingToFileDetails()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var inbox = ctx.Services.GetRequiredService<NotificationInboxService>();
        var fileId = Guid.NewGuid();
        inbox.Add(new FileDownloadedNotification(fileId, "contrato.pdf", DateTimeOffset.UtcNow));

        var cut = ctx.RenderComponent<Notifications>();

        Assert.Contains("contrato.pdf", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='/files/{fileId}']"));
    }

    [Fact]
    public void NewNotification_WhilePageIsOpen_UpdatesWithoutAManualRefresh()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var inbox = ctx.Services.GetRequiredService<NotificationInboxService>();
        var cut = ctx.RenderComponent<Notifications>();

        Assert.Contains("Nenhuma notificação ainda", cut.Markup);

        cut.InvokeAsync(() => inbox.Add(new FileDownloadedNotification(Guid.NewGuid(), "novo.pdf", DateTimeOffset.UtcNow)));

        cut.WaitForAssertion(() => Assert.Contains("novo.pdf", cut.Markup));
    }
}
