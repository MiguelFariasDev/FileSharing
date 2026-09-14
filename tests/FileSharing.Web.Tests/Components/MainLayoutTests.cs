using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Bunit;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace FileSharing.Web.Tests.Components;

public class MainLayoutTests
{
    [Fact]
    public void ClickingSair_ClearsAuthState_AndRedirectsToLogin()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));
        ctx.TokenProvider.SetToken("jwt-token", DateTimeOffset.UtcNow.AddHours(1));

        var cut = ctx.RenderComponent<MainLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder => builder.AddContent(0, "conteúdo"))));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Sair")).Click();

        Assert.False(ctx.TokenProvider.IsAuthenticated);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/login", navigation.Uri);
    }

    [Fact]
    public void ShowsTheAuthenticatedUsersEmail()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));

        var cut = ctx.RenderComponent<MainLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder => builder.AddContent(0, "conteúdo"))));

        Assert.Contains("owner@example.com", cut.Markup);
    }

    [Fact]
    public void HasAWorkingMobileNavToggle_TargetingTheSameCollapseItControls()
    {
        // Regression guard: the navbar-collapse previously had no toggler at all, so below
        // Bootstrap's md breakpoint the "Meus arquivos" link (and the whole Sair/user section,
        // both inside the collapse) were permanently invisible with no way to reveal them.
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));

        var cut = ctx.RenderComponent<MainLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder => builder.AddContent(0, "conteúdo"))));

        var toggler = cut.Find("button.navbar-toggler");
        var target = toggler.GetAttribute("data-bs-target");
        Assert.False(string.IsNullOrWhiteSpace(target));
        Assert.NotNull(cut.Find(target!)); // throws if no element matches the toggler's own target selector
    }
}
