using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Pages;
using FileSharing.Web.Components.Shared;

namespace FileSharing.Web.Tests.Components;

public class FileDetailsTests
{
    private static readonly Guid ActiveFileId = Guid.NewGuid();
    private static readonly Guid ExpiredFileId = Guid.NewGuid();

    private static readonly FileSummaryResponse ActiveFile = new(
        ActiveFileId, "document.pdf", "application/pdf", 2048, false, "Active",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 3, false);

    private static readonly FileSummaryResponse ExpiredFile = new(
        ExpiredFileId, "old.pdf", "application/pdf", 512, false, "Expired",
        DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), 0, true);

    private static HttpResponseMessage MyFilesResponder(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/files/mine"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { ActiveFile, ExpiredFile }) };

        if (request.RequestUri.AbsolutePath.EndsWith("/link"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { fileId = ActiveFileId, accessToken = "tok", publicUrl = "https://fake-api.test/api/public/files/tok" })
            };

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static IRenderedComponent<FileDetails> Render(WebComponentTestContext ctx, Guid fileId) =>
        ctx.RenderComponent<FileDetails>(p => p.Add(c => c.FileId, fileId));

    [Fact]
    public void ExistingFile_RendersItsMetadata()
    {
        using var ctx = new WebComponentTestContext(MyFilesResponder);
        var cut = Render(ctx, ActiveFileId);

        Assert.Contains("document.pdf", cut.Find("h1").TextContent);
        Assert.Contains("application/pdf", cut.Markup);
        Assert.Contains("2 KB", cut.Markup);
        Assert.Contains("3", cut.Markup); // download count
    }

    [Fact]
    public void UnknownFileId_ShowsNotFoundState_WithALinkBackToDashboard()
    {
        using var ctx = new WebComponentTestContext(MyFilesResponder);
        var cut = Render(ctx, Guid.NewGuid());

        var errorState = cut.FindComponent<ErrorState>();
        Assert.Equal(ErrorStateKind.NotFound, errorState.Instance.Kind);
        Assert.NotNull(cut.Find("a[href='/dashboard']"));
    }

    [Fact]
    public void ExpiredFile_ShowsExpiredState_AndHidesLinkGeneration()
    {
        using var ctx = new WebComponentTestContext(MyFilesResponder);
        var cut = Render(ctx, ExpiredFileId);

        var errorState = cut.FindComponent<ErrorState>();
        Assert.Equal(ErrorStateKind.Expired, errorState.Instance.Kind);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Gerar"));
    }

    [Fact]
    public void ActiveFileWithoutLink_ShowsGerarLink_AndGeneratesOnClick()
    {
        using var ctx = new WebComponentTestContext(MyFilesResponder);
        var cut = Render(ctx, ActiveFileId);

        cut.Find("button").Click(); // "Gerar link" (no existing link yet, no confirmation needed)

        Assert.Contains("https://fake-api.test/api/public/files/tok", cut.Markup);
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
    }

    [Fact]
    public void RendersALinkToTheDownloadHistoryPage()
    {
        using var ctx = new WebComponentTestContext(MyFilesResponder);
        var cut = Render(ctx, ActiveFileId);

        Assert.NotNull(cut.Find($"a[href='/files/{ActiveFileId}/history']"));
    }

    [Fact]
    public void GenerateLinkFailure_ShowsAToast_NeverCrashesThePage()
    {
        using var ctx = new WebComponentTestContext(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/link")
                ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { title = "O arquivo expirou.", code = "FILE_EXPIRED" }) }
                : MyFilesResponder(request));
        var cut = Render(ctx, ActiveFileId);
        var toastCut = ctx.RenderComponent<ToastContainer>();

        cut.Find("button").Click();

        toastCut.WaitForAssertion(() => Assert.Contains("O arquivo expirou.", toastCut.Markup));
    }
}
