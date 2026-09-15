using Bunit;
using FileSharing.Web.Components.Pages;

namespace FileSharing.Web.Tests.Components;

public class LandingTests : TestContext
{
    [Fact]
    public void RendersTheHeroAndBothPrimaryCallsToAction()
    {
        var cut = RenderComponent<Landing>();

        Assert.Contains("Compartilhe arquivos", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find("a[href='/register']"));
        Assert.NotNull(cut.Find("a[href='/login']"));
    }

    [Fact]
    public void NeverRevealsInternalInfrastructureDetails()
    {
        var cut = RenderComponent<Landing>();

        Assert.DoesNotContain("S3", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localstack", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendersTheFourBenefitCards()
    {
        var cut = RenderComponent<Landing>();

        Assert.Equal(4, cut.FindAll(".landing-benefit-card").Count);
    }

    [Fact]
    public void RendersTheThreeHowItWorksSteps()
    {
        var cut = RenderComponent<Landing>();

        Assert.Equal(3, cut.FindAll(".landing-step").Count);
    }

    [Fact]
    public void FinalCallToActionLinksToRegister()
    {
        var cut = RenderComponent<Landing>();

        var ctaSection = cut.Find(".landing-cta");
        Assert.NotNull(ctaSection.QuerySelector("a[href='/register']"));
    }
}
