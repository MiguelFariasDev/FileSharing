using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class LoginTests
{
    private static HttpResponseMessage SuccessfulAuthResponder(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/auth/login"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(1)))
            };

        if (request.RequestUri.AbsolutePath.EndsWith("/api/auth/me"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserResponse(Guid.NewGuid(), "user@example.com"))
            };

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static void FillForm(IRenderedComponent<Login> cut, string email, string password)
    {
        cut.Find("#login-email").Change(email);
        cut.Find("#login-password").Change(password);
    }

    [Fact]
    public void SuccessfulLogin_StoresTheTokenAndNavigatesToDashboard()
    {
        using var ctx = new WebComponentTestContext(SuccessfulAuthResponder);
        var cut = ctx.RenderComponent<Login>();

        FillForm(cut, "user@example.com", "SenhaForte123");
        cut.Find("form").Submit();

        Assert.True(ctx.TokenProvider.IsAuthenticated);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/dashboard", navigation.Uri);
    }

    [Fact]
    public void InvalidCredentials_ShowsAGenericMessage_NeverApiInternals()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cut = ctx.RenderComponent<Login>();

        FillForm(cut, "user@example.com", "wrong-password");
        cut.Find("form").Submit();

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Email ou senha inválidos", alert.TextContent);
        Assert.False(ctx.TokenProvider.IsAuthenticated);
    }

    [Fact]
    public void ApiUnavailable_ShowsAFriendlyNetworkMessage()
    {
        using var ctx = new WebComponentTestContext((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("simulated network failure")));
        var cut = ctx.RenderComponent<Login>();

        FillForm(cut, "user@example.com", "SenhaForte123");
        cut.Find("form").Submit();

        var alert = cut.Find(".alert-danger");
        Assert.DoesNotContain("HttpRequestException", alert.TextContent);
    }

    [Fact]
    public void RendersALinkToTheRegisterPage()
    {
        using var ctx = new WebComponentTestContext(SuccessfulAuthResponder);
        var cut = ctx.RenderComponent<Login>();

        Assert.NotNull(cut.Find("a[href='/register']"));
    }
}
