using FileSharing.Application.DTOs.Auth;
using FileSharing.Web.Services;

namespace FileSharing.Web.Tests.Services;

public class ApiAuthenticationStateProviderTests
{
    [Fact]
    public async Task NewInstance_IsAnonymous()
    {
        var sut = new ApiAuthenticationStateProvider();

        var state = await sut.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task MarkUserAsAuthenticated_ProducesAnAuthenticatedPrincipal_WithTheUsersEmailAndId()
    {
        var sut = new ApiAuthenticationStateProvider();
        var user = new UserResponse(Guid.NewGuid(), "owner@example.com");

        sut.MarkUserAsAuthenticated(user);
        var state = await sut.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("owner@example.com", state.User.Identity!.Name);
        Assert.Equal(user.Id.ToString(), state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task MarkUserAsLoggedOut_ReturnsToAnonymous()
    {
        var sut = new ApiAuthenticationStateProvider();
        sut.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));

        sut.MarkUserAsLoggedOut();
        var state = await sut.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public void MarkUserAsAuthenticated_RaisesAuthenticationStateChanged()
    {
        var sut = new ApiAuthenticationStateProvider();
        var raised = false;
        sut.AuthenticationStateChanged += _ => raised = true;

        sut.MarkUserAsAuthenticated(new UserResponse(Guid.NewGuid(), "owner@example.com"));

        Assert.True(raised);
    }
}
