using FileSharing.Web.Services;

namespace FileSharing.Web.Tests.Services;

public class AuthTokenProviderTests
{
    [Fact]
    public void NewInstance_IsNotAuthenticated()
    {
        var sut = new AuthTokenProvider();

        Assert.False(sut.IsAuthenticated);
        Assert.Null(sut.AccessToken);
    }

    [Fact]
    public void SetToken_MakesItAuthenticated_AndStoresTheToken()
    {
        var sut = new AuthTokenProvider();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        sut.SetToken("jwt-value", expiresAt);

        Assert.True(sut.IsAuthenticated);
        Assert.Equal("jwt-value", sut.AccessToken);
        Assert.Equal(expiresAt, sut.ExpiresAt);
    }

    [Fact]
    public void Clear_MakesItUnauthenticated_AndRemovesTheToken()
    {
        var sut = new AuthTokenProvider();
        sut.SetToken("jwt-value", DateTimeOffset.UtcNow.AddHours(1));

        sut.Clear();

        Assert.False(sut.IsAuthenticated);
        Assert.Null(sut.AccessToken);
        Assert.Null(sut.ExpiresAt);
    }
}
