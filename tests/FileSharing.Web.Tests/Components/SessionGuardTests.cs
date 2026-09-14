using System.Net;
using Bunit;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Components.Shared;
using FileSharing.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.Web.Tests.Components;

public class SessionGuardTests
{
    private static void TriggerSessionExpired(WebComponentTestContext ctx, IRenderedFragment cut)
    {
        // Goes through a real 401 response (matching how the Api actually behaves) instead of
        // invoking the SessionExpired event via reflection, so this exercises the exact same
        // path FileSharingApiClient uses in production.
        var apiClient = ctx.Services.GetRequiredService<FileSharingApiClient>();
        cut.InvokeAsync(() => apiClient.GetMyFilesAsync());
    }

    [Fact]
    public void SessionExpired_ClearsTokenAndAuthState_Immediately()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        ctx.TokenProvider.SetToken("jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        ctx.AuthStateProvider.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));
        var cut = ctx.RenderComponent<SessionGuard>();

        TriggerSessionExpired(ctx, cut);

        cut.WaitForAssertion(() => Assert.False(ctx.TokenProvider.IsAuthenticated));
    }

    [Fact]
    public void SessionExpired_ShowsAToast_BeforeNavigatingAway()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cut = ctx.RenderComponent<SessionGuard>();
        var toastCut = ctx.RenderComponent<ToastContainer>();

        TriggerSessionExpired(ctx, cut);

        toastCut.WaitForAssertion(() => Assert.Contains("sessão expirou", toastCut.Markup));
    }

    [Fact]
    public void SessionExpired_EventuallyNavigatesToLogin()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cut = ctx.RenderComponent<SessionGuard>();
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();

        TriggerSessionExpired(ctx, cut);

        // The redirect is deliberately delayed a couple of seconds so the toast above has time
        // to actually render before MainLayout (and its ToastContainer) is torn down. NavigateTo
        // does not itself trigger a component render, so WaitForAssertion (which only re-checks
        // on render events) would never re-poll here — a plain wall-clock poll is used instead.
        var deadline = DateTime.UtcNow.Add(TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline && !navigation.Uri.EndsWith("/login"))
            Thread.Sleep(50);

        Assert.EndsWith("/login", navigation.Uri);
    }

    [Fact]
    public void MultipleSessionExpiredEventsInQuickSuccession_OnlyShowOneToast()
    {
        // Two authenticated calls in flight at once (e.g. GetMyFilesAsync + GetDownloadHistoryAsync)
        // can both come back 401 independently — the user should see one message, not a stack of
        // identical toasts.
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cut = ctx.RenderComponent<SessionGuard>();
        var toastCut = ctx.RenderComponent<ToastContainer>();
        var apiClient = ctx.Services.GetRequiredService<FileSharingApiClient>();

        cut.InvokeAsync(async () =>
        {
            await apiClient.GetMyFilesAsync();
            await apiClient.GetMyFilesAsync();
        });

        toastCut.WaitForAssertion(() => Assert.Contains("sessão expirou", toastCut.Markup));
        Assert.Single(toastCut.FindAll(".toast"));
    }
}
