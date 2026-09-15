using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Pages;

namespace FileSharing.Web.Tests.Components;

public class UploadTests
{
    private static readonly Guid FileId = Guid.NewGuid();
    private const string PresignedUploadUrl = "https://fake-s3.test/bucket/upload-key";

    private static HttpResponseMessage HappyPathResponder(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/api/files/upload") && request.Method == HttpMethod.Post)
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new InitiateUploadResponse(FileId, PresignedUploadUrl, DateTimeOffset.UtcNow.AddMinutes(15)))
            };

        if (path.EndsWith("/complete") && request.Method == HttpMethod.Post)
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CompleteUploadResponse(FileId, "document.pdf", 1024, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))
            };

        if (path.EndsWith("/link") && request.Method == HttpMethod.Post)
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { fileId = FileId, accessToken = "tok", publicUrl = "https://fake-api.test/api/public/files/tok" })
            };

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static void SetupSuccessfulSelection(WebComponentTestContext ctx, string name = "document.pdf", long size = 1024, string contentType = "application/pdf") =>
        ctx.JSInterop.Setup<Upload.SelectionMeta?>("fileSharingUpload.prepareFile", _ => true)
            .SetResult(new Upload.SelectionMeta("handle-1", name, size, contentType));

    private static void SetupSuccessfulUpload(WebComponentTestContext ctx) =>
        ctx.JSInterop.Setup<Upload.UploadJsResult>("fileSharingUpload.uploadToPresignedUrl", _ => true)
            .SetResult(new Upload.UploadJsResult(true, 200));

    private static void PickFile(IRenderedComponent<Upload> cut) => cut.Find("#upload-file-input").Change("");

    // --- Renderização ---

    [Fact]
    public void InitialState_RendersPickerButtonsAndHint()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        var cut = ctx.RenderComponent<Upload>();

        Assert.Contains("Enviar arquivo", cut.Find("h1").TextContent);
        Assert.Contains("Selecionar arquivo", cut.Markup);
        Assert.Contains("Selecionar pasta", cut.Markup);
        Assert.Contains("Tamanho máximo", cut.Markup);
    }

    [Fact]
    public void TriggerFilePickerButton_OpensTheHiddenFileInput()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        var clickInvocation = ctx.JSInterop.SetupVoid("fileSharingUpload.triggerClick", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        cut.Find("button").Click(); // "Selecionar arquivo" is the first button

        clickInvocation.VerifyInvoke("fileSharingUpload.triggerClick");
    }

    // --- Validação ---

    [Fact]
    public void DisallowedContentType_ShowsError_AndNeverCallsTheApi()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        ctx.JSInterop.Setup<Upload.SelectionMeta?>("fileSharingUpload.prepareFile", _ => true)
            .SetResult(new Upload.SelectionMeta("handle-1", "malware.exe", 1024L, "application/x-msdownload"));
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        Assert.Contains("Tipo de arquivo não permitido", cut.Find(".alert-danger").TextContent);
        Assert.Empty(ctx.Handler.Requests);
    }

    [Fact]
    public void OversizedFile_ShowsError_AndNeverCallsTheApi()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        ctx.JSInterop.Setup<Upload.SelectionMeta?>("fileSharingUpload.prepareFile", _ => true)
            .SetResult(new Upload.SelectionMeta("handle-1", "huge.pdf", 6L * 1024 * 1024 * 1024, "application/pdf"));
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        Assert.Contains("excede o limite", cut.Find(".alert-danger").TextContent);
        Assert.Empty(ctx.Handler.Requests);
    }

    // --- Sucesso (fluxo completo) ---

    [Fact]
    public void ValidFile_RunsTheFullFlow_AndEndsInCompletedWithALink()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        SetupSuccessfulSelection(ctx);
        SetupSuccessfulUpload(ctx);
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        cut.WaitForAssertion(() => Assert.Contains("Upload concluído", cut.Find(".alert-success").TextContent));
        Assert.Contains("https://fake-api.test/api/public/files/tok", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='/files/{FileId}']"));

        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/api/files/upload"));
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/complete"));
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
    }

    [Fact]
    public void ValidFile_NeverSendsFileBytesThroughTheApiHttpClient()
    {
        // The whole point of the direct-to-S3 rewrite: FileSharingApiClient's HttpClient (the
        // only one this page holds) is used solely for initiate/complete/link JSON calls —
        // the PUT itself happens in JS, invisible to this .NET HttpClient entirely.
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        SetupSuccessfulSelection(ctx);
        SetupSuccessfulUpload(ctx);
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        Assert.DoesNotContain(ctx.Handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void FolderSelection_ZipsClientSide_ThenInitiatesAsAFolderUpload()
    {
        string? initiateBody = null;
        using var ctx = new WebComponentTestContext(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/files/upload") && request.Method == HttpMethod.Post)
                initiateBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            return HappyPathResponder(request);
        });
        ctx.JSInterop.Setup<Upload.SelectionMeta?>("fileSharingUpload.prepareFolderZip", _ => true)
            .SetResult(new Upload.SelectionMeta("handle-2", "pasta-20260101-000000.zip", 2048L, "application/zip"));
        SetupSuccessfulUpload(ctx);
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        cut.Find("#upload-folder-input").Change("");

        cut.WaitForAssertion(() => Assert.Contains("Upload concluído", cut.Find(".alert-success").TextContent));

        Assert.Contains("\"isFolder\":true", initiateBody);
    }

    // --- Erros ---

    [Fact]
    public void InitiateUploadFailure_ShowsFailedState_AndNeverCallsS3()
    {
        using var ctx = new WebComponentTestContext(
            _ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { title = "Não foi possível iniciar o envio.", code = "FILE_UPLOAD_INVALID_STATE" }) });
        SetupSuccessfulSelection(ctx);
        var uploadInvocation = ctx.JSInterop.Setup<Upload.UploadJsResult>("fileSharingUpload.uploadToPresignedUrl", _ => true).SetResult(new Upload.UploadJsResult(true, 200));
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        cut.WaitForAssertion(() => Assert.Contains("Não foi possível iniciar o envio", cut.Find(".alert-danger").TextContent));
        Assert.NotNull(cut.Find("button")); // "Tentar novamente"
        uploadInvocation.VerifyNotInvoke("fileSharingUpload.uploadToPresignedUrl");
    }

    [Fact]
    public void S3PutFailure_ShowsFailedState_AndNeverCallsComplete()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        SetupSuccessfulSelection(ctx);
        ctx.JSInterop.Setup<Upload.UploadJsResult>("fileSharingUpload.uploadToPresignedUrl", _ => true).SetResult(new Upload.UploadJsResult(false, 403));
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        cut.WaitForAssertion(() => Assert.Contains("Não foi possível concluir o envio", cut.Find(".alert-danger").TextContent));
        Assert.DoesNotContain(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/complete"));
    }

    [Fact]
    public void CompleteUploadFailure_ShowsFailedState()
    {
        using var ctx = new WebComponentTestContext(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/complete") && request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { title = "Este upload não está pendente de confirmação.", code = "FILE_UPLOAD_INVALID_STATE" }) }
                : HappyPathResponder(request));
        SetupSuccessfulSelection(ctx);
        SetupSuccessfulUpload(ctx);
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);

        cut.WaitForAssertion(() => Assert.Contains("não está pendente de confirmação", cut.Find(".alert-danger").TextContent));
    }

    [Fact]
    public void FailedState_TentarNovamente_ResetsToThePicker()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        SetupSuccessfulSelection(ctx);
        ctx.JSInterop.SetupVoid("fileSharingUpload.cleanup", _ => true).SetVoidResult();
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alert-danger")));

        cut.Find("button").Click();

        Assert.Contains("Selecionar arquivo", cut.Markup);
        Assert.Empty(cut.FindAll(".alert-danger"));
    }

    // --- Progresso ---

    [Fact]
    public void UploadProgressCallback_UpdatesTheProgressBar()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        SetupSuccessfulSelection(ctx);
        // Deliberately left incomplete (no SetResult yet) — freezes the flow in the Uploading
        // stage so the progress bar is actually on screen when OnUploadProgress fires, exactly
        // like the real XHR upload staying in flight until the browser reports completion.
        var uploadInvocation = ctx.JSInterop.Setup<Upload.UploadJsResult>("fileSharingUpload.uploadToPresignedUrl", _ => true);
        var cut = ctx.RenderComponent<Upload>();

        PickFile(cut);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".progress-bar")));

        cut.InvokeAsync(() => cut.Instance.OnUploadProgress(0.42));

        cut.WaitForAssertion(() => Assert.Contains("42%", cut.Find(".progress-bar").TextContent));

        uploadInvocation.SetResult(new Upload.UploadJsResult(true, 200));
    }

    [Fact]
    public void RendersALinkBackToDashboard()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder);
        var cut = ctx.RenderComponent<Upload>();

        Assert.NotNull(cut.Find("a[href='/dashboard']"));
    }
}
