using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class RegisterTests
{
    [Fact]
    public void AlreadyAuthenticated_RedirectsStraightToDashboard_WithoutShowingTheForm()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.TokenProvider.SetToken("jwt-token", DateTimeOffset.UtcNow.AddHours(1));

        ctx.RenderComponent<Register>();

        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/dashboard", navigation.Uri);
    }

    private static void FillForm(IRenderedComponent<Register> cut, string email, string password)
    {
        cut.Find("#register-email").Change(email);
        cut.Find("#register-password").Change(password);
    }

    [Fact]
    public void SuccessfulRegistration_ShowsAConfirmation_AndNeverAuthenticatesTheUser()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new UserResponse(Guid.NewGuid(), "new-user@example.com"))
        });
        var cut = ctx.RenderComponent<Register>();

        FillForm(cut, "new-user@example.com", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.Contains("Cadastro concluído", cut.Find(".alert-success").TextContent);
        // Registering must never leave the caller authenticated — POST /api/auth/register
        // returns a UserResponse, not a token (see FileSharingApiClient.RegisterAsync remarks).
        Assert.False(ctx.TokenProvider.IsAuthenticated);
    }

    [Fact]
    public void DuplicateEmail_ShowsTheApisMessage()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { message = "Não foi possível concluir o cadastro." })
        });
        var cut = ctx.RenderComponent<Register>();

        FillForm(cut, "existing@example.com", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.NotEmpty(cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void RendersALinkToTheLoginPage()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cut = ctx.RenderComponent<Register>();

        Assert.NotNull(cut.Find("a[href='/login']"));
    }
}
