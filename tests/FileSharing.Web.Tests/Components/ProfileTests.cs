using System.Net;
using Bunit;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class ProfileTests
{
    [Fact]
    public void RendersTheAuthenticatedUsersEmail()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));

        var cut = ctx.RenderComponent<Profile>();

        Assert.Contains("owner@example.com", cut.Markup);
    }

    [Fact]
    public void ClickingSair_ClearsAuthState_AndRedirectsToLogin()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));
        ctx.TokenProvider.SetToken("jwt-token", DateTimeOffset.UtcNow.AddHours(1));

        var cut = ctx.RenderComponent<Profile>();
        cut.Find("button").Click();

        Assert.False(ctx.TokenProvider.IsAuthenticated);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/login", navigation.Uri);
    }

    [Fact]
    public void NeverExposesTheJwtOrAnyInternalToken()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));
        ctx.TokenProvider.SetToken("super-secret-jwt-value", DateTimeOffset.UtcNow.AddHours(1));

        var cut = ctx.RenderComponent<Profile>();

        Assert.DoesNotContain("super-secret-jwt-value", cut.Markup);
    }
}
