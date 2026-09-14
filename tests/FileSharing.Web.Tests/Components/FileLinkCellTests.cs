using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Shared;

namespace FileSharing.Web.Tests.Components;

public class FileLinkCellTests : TestContext
{
    private static FileSummaryResponse ActiveFile(bool hasPublicLink = false) =>
        new(Guid.NewGuid(), "doc.pdf", "application/pdf", 1024, false, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24), 0, hasPublicLink);

    [Fact]
    public void PendingFile_ShowsProcessando_NeverAGenerateButton()
    {
        var summary = new FileSummaryResponse(Guid.NewGuid(), "doc.pdf", "application/pdf", 1024, false, "PendingUpload", null, null, 0, false);

        var cut = RenderComponent<FileLinkCell>(p => p.Add(c => c.Summary, summary));

        Assert.Contains("Processando", cut.Markup);
        Assert.DoesNotContain("<button", cut.Markup);
    }

    [Fact]
    public void ExpiredFile_ShowsPlaceholder_NeverAGenerateButton()
    {
        var summary = ActiveFile() with { Status = "Expired" };

        var cut = RenderComponent<FileLinkCell>(p => p.Add(c => c.Summary, summary));

        Assert.DoesNotContain("<button", cut.Markup);
    }

    [Fact]
    public void EffectivelyExpiredActiveFile_ShowsPlaceholder_EvenThoughStatusIsStillActive()
    {
        // Status hasn't been flipped by Hangfire yet, but the client's own clock already
        // considers it expired — the link controls must not offer to generate a new one either.
        var summary = ActiveFile();

        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, summary)
            .Add(c => c.IsEffectivelyExpired, true));

        Assert.DoesNotContain("<button", cut.Markup);
    }

    [Fact]
    public void NoLinkYet_ShowsGerarLink_NeverGerarNovoLink()
    {
        var cut = RenderComponent<FileLinkCell>(p => p.Add(c => c.Summary, ActiveFile(hasPublicLink: false)));

        Assert.Contains("Gerar link", cut.Markup);
        Assert.DoesNotContain("Gerar novo link", cut.Markup);
    }

    [Fact]
    public void AlreadyHasALink_ShowsGerarNovoLink()
    {
        var cut = RenderComponent<FileLinkCell>(p => p.Add(c => c.Summary, ActiveFile(hasPublicLink: true)));

        Assert.Contains("Gerar novo link", cut.Markup);
    }

    [Fact]
    public void Busy_ShowsGerandoText_AndDisablesTheButton()
    {
        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, ActiveFile())
            .Add(c => c.LinkBusy, true));

        Assert.Contains("Gerando...", cut.Markup);
        Assert.Contains("disabled", cut.Find("button").OuterHtml);
    }

    [Fact]
    public void Busy_WhileConfirmingRegeneration_AlsoShowsGerandoText_AndDisablesBothButtons()
    {
        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, ActiveFile(hasPublicLink: true))
            .Add(c => c.ShowRegenerateConfirm, true)
            .Add(c => c.LinkBusy, true));

        Assert.Contains("Gerando...", cut.Markup);
        Assert.All(cut.FindAll("button"), b => Assert.Contains("disabled", b.OuterHtml));
    }

    [Fact]
    public void ClickingGenerate_RaisesTheCallback()
    {
        var clicked = false;
        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, ActiveFile())
            .Add(c => c.OnGenerateOrRegenerateClicked, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void ConfirmDialog_CancelRaisesOnCancelRegenerate_NeverOnConfirm()
    {
        var confirmed = false;
        var cancelled = false;
        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, ActiveFile(hasPublicLink: true))
            .Add(c => c.ShowRegenerateConfirm, true)
            .Add(c => c.OnConfirmRegenerate, () => confirmed = true)
            .Add(c => c.OnCancelRegenerate, () => cancelled = true));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Cancelar")).Click();

        Assert.True(cancelled);
        Assert.False(confirmed);
    }

    [Fact]
    public void CopyButton_PassesTheExactLinkToTheCallback()
    {
        string? copied = null;
        var cut = RenderComponent<FileLinkCell>(p => p
            .Add(c => c.Summary, ActiveFile(hasPublicLink: true))
            .Add(c => c.GeneratedLink, "https://api.test/api/public/files/abc")
            .Add(c => c.OnCopyLink, (string link) => copied = link));

        cut.Find("button[aria-label='Copiar link']").Click();

        Assert.Equal("https://api.test/api/public/files/abc", copied);
    }
}
