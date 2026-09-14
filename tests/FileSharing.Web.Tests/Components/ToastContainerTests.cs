using Microsoft.Extensions.DependencyInjection;
using Bunit;
using FileSharing.Web.Components.Shared;
using FileSharing.Web.Services;

namespace FileSharing.Web.Tests.Components;

public class ToastContainerTests : TestContext
{
    public ToastContainerTests()
    {
        Services.AddSingleton(new ToastService());
    }

    [Fact]
    public void ShowSuccess_RendersANonBlockingToast_WithTheGivenText()
    {
        var toastService = Services.GetRequiredService<ToastService>();
        var cut = RenderComponent<ToastContainer>();

        cut.InvokeAsync(() => toastService.ShowSuccess("Seu arquivo relatorio.pdf foi baixado."));

        cut.WaitForAssertion(() => Assert.Contains("relatorio.pdf foi baixado", cut.Markup));
        // Non-blocking: a toast is a rendered element, never window.alert/confirm (which bUnit
        // has no concept of — a real alert() would hang the test runner instead).
        Assert.Contains("toast", cut.Markup);
    }

    [Fact]
    public void MultipleToasts_AreAllShownIndependently()
    {
        var toastService = Services.GetRequiredService<ToastService>();
        var cut = RenderComponent<ToastContainer>();

        cut.InvokeAsync(() =>
        {
            toastService.ShowSuccess("Primeiro arquivo baixado.");
            toastService.ShowSuccess("Segundo arquivo baixado.");
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Primeiro arquivo baixado.", cut.Markup);
            Assert.Contains("Segundo arquivo baixado.", cut.Markup);
        });
    }
}
