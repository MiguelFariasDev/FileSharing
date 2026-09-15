using System.Net;
using System.Net.Http.Json;
using Bunit;
using FileSharing.Application.DTOs.Files;
using FileSharing.Web.Components.Pages;

namespace FileSharing.Web.Tests.Components;

public class DashboardTests
{
    private static List<FileSummaryResponse> ThreeFilesOneOfEachStatus() =>
    [
        new(Guid.NewGuid(), "active.pdf", "application/pdf", 2048, false, "Active", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(23), 3, true),
        new(Guid.NewGuid(), "expiring-soon.pdf", "application/pdf", 512, false, "Active", DateTimeOffset.UtcNow.AddHours(-23), DateTimeOffset.UtcNow.AddMinutes(30), 1, true),
        new(Guid.NewGuid(), "expired.pdf", "application/pdf", 4096, false, "Expired", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), 5, true)
    ];

    private static HttpResponseMessage DefaultResponder(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/api/files/mine")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ThreeFilesOneOfEachStatus()) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    [Fact]
    public void RendersSummaryCounts_ForEachStatusBucket()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("Arquivos ativos", cut.Markup);
        Assert.Contains("Expirando em breve", cut.Markup);
        Assert.Contains("Arquivos expirados", cut.Markup);
        Assert.Contains("Downloads recentes", cut.Markup);
    }

    [Fact]
    public void RendersQuickActionLinks()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        Assert.NotNull(cut.Find("a[href='/upload']"));
        Assert.NotNull(cut.Find("a[href='/files']"));
        Assert.NotNull(cut.Find("a[href='/notifications']"));
    }

    [Fact]
    public void RendersRecentFiles_LinkingToTheirDetailsPage()
    {
        using var ctx = new WebComponentTestContext(DefaultResponder);
        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("active.pdf", cut.Markup);
        Assert.NotEmpty(cut.FindAll("a.list-group-item"));
    }

    [Fact]
    public void NoFiles_ShowsAnEmptyStateWithAnUploadLink()
    {
        using var ctx = new WebComponentTestContext(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<FileSummaryResponse>()) });
        var cut = ctx.RenderComponent<Dashboard>();

        Assert.Contains("ainda não possui arquivos", cut.Markup);
        Assert.NotNull(cut.Find("a[href='/upload']"));
    }

    [Fact]
    public void ApiFailure_ShowsAFriendlyError_WithARetryOption()
    {
        using var ctx = new WebComponentTestContext(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = ctx.RenderComponent<Dashboard>();

        var alert = cut.Find(".alert-danger");
        Assert.NotNull(alert);
        Assert.NotNull(cut.Find("button"));
    }
}
