using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.DTOs.Notifications;
using FileSharing.Web.Components.Pages;
using FileSharing.Web.Models;
using FileSharing.Web.Services.Notifications;

namespace FileSharing.Web.Tests.Components;

public class DashboardTests
{
    private static readonly Guid ActiveFileId = Guid.NewGuid();
    private static readonly Guid PendingFileId = Guid.NewGuid();
    private static readonly Guid ExpiredFileId = Guid.NewGuid();

    private static List<FileSummaryResponse> ThreeFilesOneOfEachStatus() =>
    [
        new(ActiveFileId, "active.pdf", "application/pdf", 2048, false, "Active", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 3, true),
        new(PendingFileId, "pending.pdf", "application/pdf", 1024, false, "PendingUpload", null, null, 0, false),
        new(ExpiredFileId, "expired.pdf", "application/pdf", 4096, false, "Expired", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), 5, true)
    ];

    private static HttpResponseMessage DefaultResponder(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/api/files/mine"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ThreeFilesOneOfEachStatus()) };

        if (path.EndsWith("/downloads"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<DownloadHistoryEntryResponse> { new(DateTimeOffset.UtcNow.AddMinutes(-10)) })
            };

        if (path.EndsWith("/link"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PublicLinkResponse(ActiveFileId, "new-token", "https://api.test/api/public/files/new-token"))
            };

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static void RaiseFileDownloaded(SignalRNotificationService service, FileDownloadedNotification notification)
    {
        var field = typeof(SignalRNotificationService)
            .GetField(nameof(SignalRNotificationService.FileDownloaded), BindingFlags.NonPublic | BindingFlags.Instance);

        var handler = (MulticastDelegate?)field?.GetValue(service);
        handler?.DynamicInvoke(notification);
    }

    [Fact]
    public void RendersEachFileWithItsOwnStatusLabel()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        var text = cut.Markup;
        Assert.Contains("active.pdf", text);
        Assert.Contains("Ativo", text);
        Assert.Contains("pending.pdf", text);
        Assert.Contains("Processando", text);
        Assert.Contains("expired.pdf", text);
        Assert.Contains("Expirado", text);
    }

    [Fact]
    public void StatusIsNeverConveyedByColorAlone()
    {
        // Accessibility requirement: Active/Expired/Pending must be readable as text, not only
        // inferable from a CSS color class.
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        var badges = cut.FindAll(".badge");
        Assert.All(badges, badge => Assert.False(string.IsNullOrWhiteSpace(badge.TextContent)));
    }

    [Fact]
    public void ShowsRemainingTimeForActiveFiles()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("restantes", cut.Markup);
    }

    [Fact]
    public void FileExpiringInLessThanAMinute_ShowsMenosDeUmMinuto_NotZeroMin()
    {
        var almostExpiredId = Guid.NewGuid();
        using var ctx = new WebComponentTestContext(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/api/files/mine")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<FileSummaryResponse>
                    {
                        new(almostExpiredId, "quase.pdf", "application/pdf", 1024, false, "Active", DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow.AddSeconds(30), 0, false)
                    })
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("menos de 1 min", cut.Markup);
        Assert.DoesNotContain("0min restantes", cut.Markup);
    }

    [Fact]
    public void ExpiredFile_ShowsExpiradoAsTheRemainingTimeLabel_NotADash()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        // ExpiredFileId (seeded by DefaultResponder) is genuinely Expired — the "Expira em /
        // restante" cell specifically (6th column) should read "Expirado", not "—".
        var row = cut.FindAll("tr").Single(r => r.TextContent.Contains("expired.pdf"));
        var remainingTimeCell = row.QuerySelectorAll("td")[5];
        Assert.Equal("Expirado", remainingTimeCell.TextContent);
    }

    [Fact]
    public void NoFiles_ShowsTheEmptyStateMessage_WithoutAFakeUploadButton()
    {
        using var ctx = new WebComponentTestContext(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/api/files/mine")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<FileSummaryResponse>()) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("Você ainda não possui arquivos", cut.Markup);
        // No fake Web upload affordance — upload stays a Mobile-only responsibility (Etapa 3).
        Assert.DoesNotContain("type=\"file\"", cut.Markup);
        Assert.DoesNotContain("Enviar arquivo", cut.Markup);
    }

    [Fact]
    public void ApiFailure_ShowsAFriendlyError_WithARetryOption()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = ctx.RenderComponent<Dashboard>();

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Tentar novamente", alert.TextContent);
    }

    [Fact]
    public void FileDownloadedEvent_IncrementsThatFilesDownloadCount_WithoutReloadingTheList()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();
        var notificationService = ctx.Services.GetRequiredService<SignalRNotificationService>();

        var before = ctx.Handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/api/files/mine"));

        cut.InvokeAsync(() => RaiseFileDownloaded(notificationService, new FileDownloadedNotification(ActiveFileId, "active.pdf", DateTimeOffset.UtcNow)));

        cut.WaitForAssertion(() => Assert.Contains(">4<", cut.Markup)); // 3 -> 4 downloads for the active file
        var after = ctx.Handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/api/files/mine"));
        Assert.Equal(before, after); // never a full-list reload in reaction to the event
    }

    [Fact]
    public void FileDownloadedEvent_ShowsAToastNotification()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        ctx.RenderComponent<Dashboard>();
        var toastCut = ctx.RenderComponent<FileSharing.Web.Components.Shared.ToastContainer>();
        var notificationService = ctx.Services.GetRequiredService<SignalRNotificationService>();

        toastCut.InvokeAsync(() => RaiseFileDownloaded(notificationService, new FileDownloadedNotification(ActiveFileId, "active.pdf", DateTimeOffset.UtcNow)));

        toastCut.WaitForAssertion(() => Assert.Contains("foi baixado", toastCut.Markup));
    }

    [Fact]
    public void GeneratingALinkForAFileThatAlreadyHasOne_AsksForConfirmationFirst()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        var regenerateButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Gerar novo link")).ToList();
        Assert.NotEmpty(regenerateButtons);
        regenerateButtons[0].Click();

        Assert.Contains("invalidará o link atual", cut.Markup);

        var linkRequestsBeforeConfirm = ctx.Handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
        Assert.Equal(0, linkRequestsBeforeConfirm);
    }

    [Fact]
    public void GeneratingALinkForTheFirstTime_ShowsTheNewLink_WithoutConfirmation()
    {
        var freshFileId = Guid.NewGuid();
        using var ctx = new WebComponentTestContext(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/files/mine"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<FileSummaryResponse>
                    {
                        new(freshFileId, "fresh.pdf", "application/pdf", 2048, false, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24), 0, false)
                    })
                };
            if (path.EndsWith("/link"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PublicLinkResponse(freshFileId, "first-token", "https://api.test/api/public/files/first-token"))
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var cut = ctx.RenderComponent<Dashboard>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Gerar link")).Click();

        cut.WaitForAssertion(() => Assert.Contains("first-token", cut.Markup));
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
    }

    [Fact]
    public void ConfirmingRegeneration_CallsTheExistingLinkEndpoint_AndShowsTheNewLink()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Gerar novo link")).Click();
        cut.WaitForAssertion(() => Assert.Contains("invalidará o link atual", cut.Markup));

        cut.FindAll("button").First(b => b.TextContent.Contains("Sim, gerar novo link")).Click();

        cut.WaitForAssertion(() => Assert.Contains("new-token", cut.Markup));
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
    }

    [Fact]
    public void TogglingHistory_LoadsAndDisplaysIt()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Ver histórico")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Ocultar histórico", cut.Markup));
    }
}
