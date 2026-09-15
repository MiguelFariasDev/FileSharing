using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Web.Components.Pages;

namespace FileSharing.Web.Tests.Components;

public class ForgotPasswordTests
{
    private static void FillForm(IRenderedComponent<ForgotPassword> cut, string email) =>
        cut.Find("#forgot-password-email").Change(email);

    private static HttpResponseMessage ErrorResponse(HttpStatusCode statusCode, string code, string title) =>
        new(statusCode) { Content = JsonContent.Create(new { title, code }) };

    // --- Renderização ---

    [Fact]
    public void InitialState_RendersTitleEmailFieldAndButton_WithoutAnyMessage()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        Assert.Contains("Esqueci minha senha", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find("#forgot-password-email"));
        Assert.Contains("Enviar instruções", cut.Find("button[type=submit]").TextContent);
        Assert.Empty(cut.FindAll(".alert-success"));
        Assert.Empty(cut.FindAll(".alert-danger"));
    }

    // --- Validação ---

    [Fact]
    public void EmptyEmail_ShowsValidationMessage_AndNeverCallsTheApi()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, string.Empty);
        cut.Find("form").Submit();

        Assert.Contains("Informe seu email", cut.Markup);
        Assert.Empty(ctx.Handler.Requests);
    }

    [Fact]
    public void InvalidEmail_ShowsValidationMessage_AndNeverCallsTheApi()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "not-an-email");
        cut.Find("form").Submit();

        Assert.Contains("Informe um email válido", cut.Markup);
        Assert.Empty(ctx.Handler.Requests);
    }

    [Fact]
    public void ValidEmail_CallsForgotPasswordEndpoint()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        Assert.Single(ctx.Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/api/auth/forgot-password"));
    }

    // --- Loading ---

    [Fact]
    public void WhileRequestIsInFlight_ButtonIsDisabledWithASpinner_PreventingDuplicateSubmission()
    {
        // The disabled attribute (rendered from _isBusy) is what stops a real browser from
        // firing a second submit — bUnit dispatches events directly and doesn't enforce HTML
        // `disabled` semantics itself, so asserting the attribute is present is the correct way
        // to verify this component protects itself, rather than simulating a click bUnit would
        // let through regardless.
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        using var ctx = new WebComponentTestContext(_ => gate.Task);
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        Assert.Contains("disabled", cut.Find("button[type=submit]").OuterHtml);
        Assert.NotEmpty(cut.FindAll(".spinner-border"));
        Assert.Single(ctx.Handler.Requests);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alert-success")));
    }

    // --- Sucesso ---

    [Fact]
    public void SuccessfulRequest_ShowsTheGenericSuccessMessage_NeverRevealingWhetherTheEmailExists()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        var alert = cut.Find(".alert-success");
        Assert.Contains("Se a conta existir", alert.TextContent);
        Assert.Empty(cut.FindAll("form"));
    }

    // --- Erros ---

    [Fact]
    public void ValidationErrorFromApi_ShowsTheApisMessage()
    {
        using var ctx = new WebComponentTestContext(_ => ErrorResponse(HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Um ou mais campos são inválidos."));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        Assert.Contains("Um ou mais campos são inválidos.", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void RateLimited_ShowsTheApisMessage()
    {
        using var ctx = new WebComponentTestContext(_ => ErrorResponse(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Muitas tentativas em pouco tempo. Aguarde um momento e tente novamente."));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        Assert.Contains("Muitas tentativas", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void InternalError_ShowsAGenericMessage_NeverInternalDetails()
    {
        using var ctx = new WebComponentTestContext(_ => ErrorResponse(HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Ocorreu um erro inesperado. Tente novamente."));
        var cut = ctx.RenderComponent<ForgotPassword>();

        FillForm(cut, "user@example.com");
        cut.Find("form").Submit();

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Ocorreu um erro inesperado", alert.TextContent);
        Assert.DoesNotContain("Exception", alert.TextContent);
    }

    [Fact]
    public void RendersALinkBackToLogin()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var cut = ctx.RenderComponent<ForgotPassword>();

        Assert.NotNull(cut.Find("a[href='/login']"));
    }
}
