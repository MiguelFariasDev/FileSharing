using Bunit;
using FileSharing.Web.Components.Pages;

namespace FileSharing.Web.Tests.Components;

public class NotFoundTests : TestContext
{
    [Fact]
    public void RendersAUsefulActionBackToTheApp()
    {
        // Regression guard for the "Error dead end" audit finding (docs/navigation-flows.md):
        // this page previously had no link anywhere, leaving a visitor with no way back in.
        var cut = RenderComponent<NotFound>();

        Assert.NotNull(cut.Find("a[href='/']"));
    }
}
