using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class ResetPasswordTests
{
    private const string ValidToken = "a-valid-reset-token";

    private static HttpResponseMessage ErrorResponse(HttpStatusCode statusCode, string code, string title) =>
        new(statusCode) { Content = JsonContent.Create(new { title, code }) };

    private static IRenderedComponent<ResetPassword> RenderWithToken(
        WebComponentTestContext ctx, string? token = ValidToken)
    {
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(token is null ? "/reset-password" : $"/reset-password?token={Uri.EscapeDataString(token)}");
        return ctx.RenderComponent<ResetPassword>();
    }

    private static void FillForm(IRenderedComponent<ResetPassword> cut, string newPassword, string confirmPassword)
    {
        cut.Find("#reset-password-new").Change(newPassword);
        cut.Find("#reset-password-confirm").Change(confirmPassword);
    }

    private static HttpResponseMessage RouteResponder(HttpRequestMessage request, HttpResponseMessage validateResponse, Func<HttpResponseMessage>? resetResponder = null)
    {
        if (request.RequestUri!.AbsolutePath.Contains("/reset-password/") && request.Method == HttpMethod.Get)
            return validateResponse;

        if (request.RequestUri!.AbsolutePath.EndsWith("/reset-password") && request.Method == HttpMethod.Post)
            return resetResponder is not null ? resetResponder() : new HttpResponseMessage(HttpStatusCode.OK);

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // --- Renderização ---

    [Fact]
    public void MissingToken_ShowsInvalidLinkMessage_WithoutValidatingAgainstTheApi()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = RenderWithToken(ctx, token: null);

        Assert.Contains("link de recuperação é inválido", cut.Find(".alert-danger").TextContent);
        Assert.Empty(ctx.Handler.Requests);
    }

    [Fact]
    public void ValidToken_RendersNewPasswordConfirmFieldsAndButton()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        Assert.NotNull(cut.Find("#reset-password-new"));
        Assert.NotNull(cut.Find("#reset-password-confirm"));
        Assert.Contains("Redefinir senha", cut.Find("button[type=submit]").TextContent);
    }

    // --- Validação ---

    [Fact]
    public void EmptyPassword_ShowsValidationMessage_AndNeverCallsResetEndpoint()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        FillForm(cut, string.Empty, string.Empty);
        cut.Find("form").Submit();

        Assert.Contains("Informe a nova senha", cut.Markup);
        Assert.DoesNotContain(ctx.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void PasswordTooShort_ShowsValidationMessage()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "short", "short");
        cut.Find("form").Submit();

        Assert.Contains("pelo menos 8 caracteres", cut.Markup);
        Assert.DoesNotContain(ctx.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void MismatchedPasswords_ShowsValidationMessage_AndNeverCallsResetEndpoint()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaDiferente123");
        cut.Find("form").Submit();

        Assert.Contains("As senhas não coincidem", cut.Markup);
        Assert.DoesNotContain(ctx.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void ValidData_CallsResetPasswordEndpoint()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Single(ctx.Handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/api/auth/reset-password"));
    }

    // --- Sucesso ---

    [Fact]
    public void SuccessfulReset_ShowsSuccessMessage_AndLoginLink_AndHidesTheForm()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, new HttpResponseMessage(HttpStatusCode.OK)));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Contains("Senha redefinida com sucesso", cut.Find(".alert-success").TextContent);
        Assert.NotNull(cut.Find("a[href='/login']"));
        Assert.Empty(cut.FindAll("form"));
    }

    // --- Error Codes ---

    [Fact]
    public void TokenValidation_InvalidCode_ShowsInvalidLinkMessage()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, ErrorResponse(HttpStatusCode.NotFound, "AUTH_PASSWORD_RESET_INVALID", "some server text")));
        var cut = RenderWithToken(ctx);

        Assert.Contains("link de recuperação é inválido", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void TokenValidation_ExpiredCode_ShowsExpiredLinkMessage_NotTheRawServerText()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, ErrorResponse(HttpStatusCode.Gone, "AUTH_PASSWORD_RESET_EXPIRED", "some server text")));
        var cut = RenderWithToken(ctx);

        var alert = cut.Find(".alert-danger");
        Assert.Contains("expirou", alert.TextContent);
        Assert.DoesNotContain("some server text", alert.TextContent);
    }

    [Fact]
    public void TokenValidation_UsedCode_ShowsAlreadyUsedMessage()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(_, ErrorResponse(HttpStatusCode.Gone, "AUTH_PASSWORD_RESET_USED", "some server text")));
        var cut = RenderWithToken(ctx);

        Assert.Contains("já foi utilizado", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Submit_TokenExpiredBetweenLoadAndSubmit_ShowsExpiredLinkMessage_NotAGenericFormError()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(
            _,
            new HttpResponseMessage(HttpStatusCode.OK),
            () => ErrorResponse(HttpStatusCode.Gone, "AUTH_PASSWORD_RESET_EXPIRED", "some server text")));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Contains("expirou", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Submit_ValidationErrorFromApi_ShowsAsAFormError_NotTheTokenErrorState()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(
            _,
            new HttpResponseMessage(HttpStatusCode.OK),
            () => ErrorResponse(HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Um ou mais campos são inválidos.")));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Contains("Um ou mais campos são inválidos.", cut.Find(".alert-danger").TextContent);
        Assert.NotEmpty(cut.FindAll("form")); // stays on the form, not the token-error state
    }

    [Fact]
    public void Submit_RateLimited_ShowsAsAFormError()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(
            _,
            new HttpResponseMessage(HttpStatusCode.OK),
            () => ErrorResponse(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Muitas tentativas em pouco tempo. Aguarde um momento e tente novamente.")));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Contains("Muitas tentativas", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Submit_InternalError_ShowsGenericFormError_NeverInternalDetails()
    {
        using var ctx = new WebComponentTestContext(_ => RouteResponder(
            _,
            new HttpResponseMessage(HttpStatusCode.OK),
            () => ErrorResponse(HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Ocorreu um erro inesperado. Tente novamente.")));
        var cut = RenderWithToken(ctx);

        FillForm(cut, "SenhaForte123", "SenhaForte123");
        cut.Find("form").Submit();

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Ocorreu um erro inesperado", alert.TextContent);
        Assert.DoesNotContain("Exception", alert.TextContent);
    }
}
