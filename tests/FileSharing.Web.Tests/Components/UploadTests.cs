using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;

namespace FileSharing.Web.Tests.Components;

public class UploadTests
{
    private static readonly Guid FileId = Guid.NewGuid();
    private const string PresignedUploadUrl = "http://fake-s3.test/bucket/upload-key";

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

    private static IRenderedComponent<InputFile> InputFile(IRenderedComponent<Upload> cut) => cut.FindComponent<InputFile>();

    private static InputFileContent SmallPdf() =>
        InputFileContent.CreateFromText("%PDF-1.4 fake content", "document.pdf", contentType: "application/pdf");

    // --- Renderização ---

    [Fact]
    public void InitialState_RendersFilePickerAndHint()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        Assert.Contains("Enviar arquivo", cut.Find("h1").TextContent);
        Assert.NotNull(cut.FindComponent<InputFile>());
        Assert.Contains("Tamanho máximo", cut.Markup);
    }

    // --- Validação ---

    [Fact]
    public void DisallowedContentType_ShowsError_AndNeverCallsTheApi()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        var executable = InputFileContent.CreateFromText("MZ fake exe", "malware.exe", contentType: "application/x-msdownload");
        InputFile(cut).UploadFiles(executable);

        Assert.Contains("Tipo de arquivo não permitido", cut.Find(".alert-danger").TextContent);
        Assert.Empty(ctx.Handler.Requests);
        Assert.Empty(ctx.UploadHandler.Requests);
    }

    // --- Sucesso (fluxo completo) ---

    [Fact]
    public void ValidFile_RunsTheFullFlow_AndEndsInCompletedWithALink()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());

        Assert.Contains("Upload concluído", cut.Find(".alert-success").TextContent);
        Assert.Contains("https://fake-api.test/api/public/files/tok", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='/files/{FileId}']"));

        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/api/files/upload"));
        Assert.Single(ctx.UploadHandler.Requests, r => r.RequestUri!.AbsoluteUri == PresignedUploadUrl && r.Method == HttpMethod.Put);
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/complete"));
        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/link"));
    }

    [Fact]
    public void ValidFile_NeverSendsTheApisBearerTokenToTheUploadUrl()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.TokenProvider.SetToken("secret-jwt", DateTimeOffset.UtcNow.AddHours(1));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());

        var uploadRequest = Assert.Single(ctx.UploadHandler.Requests);
        Assert.Null(uploadRequest.Headers.Authorization);
    }

    // --- Erros ---

    [Fact]
    public void InitiateUploadFailure_ShowsFailedState_AndNeverCallsS3()
    {
        using var ctx = new WebComponentTestContext(
            _ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { title = "Não foi possível iniciar o envio.", code = "FILE_UPLOAD_INVALID_STATE" }) },
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());

        Assert.Contains("Não foi possível iniciar o envio", cut.Find(".alert-danger").TextContent);
        Assert.NotNull(cut.Find("button")); // "Tentar novamente"
        Assert.Empty(ctx.UploadHandler.Requests);
    }

    [Fact]
    public void S3PutFailure_ShowsFailedState_AndNeverCallsComplete()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());

        Assert.Contains("Não foi possível concluir o envio", cut.Find(".alert-danger").TextContent);
        Assert.DoesNotContain(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/complete"));
    }

    [Fact]
    public void CompleteUploadFailure_ShowsFailedState()
    {
        using var ctx = new WebComponentTestContext(
            request => request.RequestUri!.AbsolutePath.EndsWith("/complete") && request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { title = "Este upload não está pendente de confirmação.", code = "FILE_UPLOAD_INVALID_STATE" }) }
                : HappyPathResponder(request),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());

        Assert.Contains("não está pendente de confirmação", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void FailedState_TentarNovamente_ResetsToThePicker()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        InputFile(cut).UploadFiles(SmallPdf());
        Assert.NotEmpty(cut.FindAll(".alert-danger"));

        cut.Find("button").Click();

        Assert.NotNull(cut.FindComponent<InputFile>());
        Assert.Empty(cut.FindAll(".alert-danger"));
    }

    [Fact]
    public void RendersALinkBackToDashboard()
    {
        using var ctx = new WebComponentTestContext(HappyPathResponder, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Upload>();

        Assert.NotNull(cut.Find("a[href='/dashboard']"));
    }
}
