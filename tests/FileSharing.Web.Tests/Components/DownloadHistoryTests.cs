using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Pages;
using FileSharing.Web.Components.Shared;

namespace FileSharing.Web.Tests.Components;

public class DownloadHistoryTests
{
    private static readonly Guid FileId = Guid.NewGuid();

    private static readonly FileSummaryResponse File = new(
        FileId, "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 2, true);

    private static HttpResponseMessage Responder(HttpRequestMessage request, IReadOnlyList<DownloadHistoryEntryResponse>? history = null, HttpStatusCode historyStatus = HttpStatusCode.OK)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/files/mine"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { File }) };

        if (request.RequestUri.AbsolutePath.EndsWith("/downloads"))
            return historyStatus == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(history ?? []) }
                : new HttpResponseMessage(historyStatus);

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static IRenderedComponent<DownloadHistory> Render(WebComponentTestContext ctx, Guid fileId) =>
        ctx.RenderComponent<DownloadHistory>(p => p.Add(c => c.FileId, fileId));

    [Fact]
    public void ExistingFile_WithEntries_RendersThemNewestOrder()
    {
        var entries = new List<DownloadHistoryEntryResponse>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-30)),
            new(DateTimeOffset.UtcNow.AddMinutes(-5)),
        };
        using var ctx = new WebComponentTestContext(r => Responder(r, entries));
        var cut = Render(ctx, FileId);

        Assert.Contains("document.pdf", cut.Find("h1").TextContent);
        Assert.Equal(2, cut.FindAll("li.list-group-item").Count);
    }

    [Fact]
    public void ExistingFile_WithNoEntries_ShowsEmptyMessage()
    {
        using var ctx = new WebComponentTestContext(r => Responder(r, []));
        var cut = Render(ctx, FileId);

        Assert.Contains("Nenhum download registrado ainda", cut.Markup);
    }

    [Fact]
    public void UnknownFileId_ShowsNotFoundState()
    {
        using var ctx = new WebComponentTestContext(r => Responder(r));
        var cut = Render(ctx, Guid.NewGuid());

        var errorState = cut.FindComponent<ErrorState>();
        Assert.Equal(ErrorStateKind.NotFound, errorState.Instance.Kind);
        Assert.NotNull(cut.Find("a[href='/dashboard']"));
    }

    [Fact]
    public void HistoryLoadFailure_ShowsErrorWithRetry()
    {
        using var ctx = new WebComponentTestContext(r => Responder(r, historyStatus: HttpStatusCode.InternalServerError));
        var cut = Render(ctx, FileId);

        var alert = cut.Find(".alert-danger");
        Assert.NotNull(alert);
        Assert.NotNull(cut.Find("button")); // "Tentar novamente"
    }

    [Fact]
    public void NeverExposesIpOrUserAgent()
    {
        var entries = new List<DownloadHistoryEntryResponse> { new(DateTimeOffset.UtcNow) };
        using var ctx = new WebComponentTestContext(r => Responder(r, entries));
        var cut = Render(ctx, FileId);

        Assert.DoesNotContain("ipAddress", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userAgent", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendersALinkBackToFileDetails()
    {
        using var ctx = new WebComponentTestContext(r => Responder(r, []));
        var cut = Render(ctx, FileId);

        Assert.NotNull(cut.Find($"a[href='/files/{FileId}']"));
    }
}
