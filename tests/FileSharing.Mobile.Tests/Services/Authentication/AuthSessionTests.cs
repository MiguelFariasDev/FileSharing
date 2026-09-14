using FileSharing.Application.DTOs.Auth;
using FileSharing.Mobile.Core.Services.Authentication;

namespace FileSharing.Mobile.Tests.Services.Authentication;

public class AuthSessionTests
{
    private static AuthSession CreateSut(out InMemorySecureStorageService storage)
    {
        storage = new InMemorySecureStorageService();
        return new AuthSession(storage);
    }

    [Fact]
    public void NewSession_IsNotAuthenticated()
    {
        var sut = CreateSut(out _);

        Assert.False(sut.IsAuthenticated);
        Assert.Null(sut.CurrentUser);
    }

    [Fact]
    public async Task SetSessionAsync_MakesItAuthenticated_AndPersiststheEssentials()
    {
        var sut = CreateSut(out var storage);
        var user = new UserResponse(Guid.NewGuid(), "user@example.com");
        var auth = new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(1));

        await sut.SetSessionAsync(auth, user);

        Assert.True(sut.IsAuthenticated);
        Assert.Equal("jwt-token", sut.AccessToken);
        Assert.Equal(user, sut.CurrentUser);
        // Persisted, not just held in memory — a fresh AuthSession over the same storage
        // recovers it (see RestoreAsync test below).
        Assert.NotNull(await storage.GetAsync("auth.access_token"));
    }

    [Fact]
    public async Task RestoreAsync_RecoversASessionPersistedByAnEarlierInstance()
    {
        var storage = new InMemorySecureStorageService();
        var user = new UserResponse(Guid.NewGuid(), "user@example.com");
        var auth = new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        await new AuthSession(storage).SetSessionAsync(auth, user);

        var restored = new AuthSession(storage);
        await restored.RestoreAsync();

        Assert.True(restored.IsAuthenticated);
        Assert.Equal("jwt-token", restored.AccessToken);
        Assert.Equal(user.Id, restored.CurrentUser!.Id);
        Assert.Equal(user.Email, restored.CurrentUser.Email);
    }

    [Fact]
    public async Task RestoreAsync_WithNothingStored_LeavesTheSessionUnauthenticated()
    {
        var sut = CreateSut(out _);

        await sut.RestoreAsync();

        Assert.False(sut.IsAuthenticated);
    }

    [Fact]
    public async Task RestoreAsync_WithAnExpiredToken_IsNotConsideredAuthenticated()
    {
        var storage = new InMemorySecureStorageService();
        var user = new UserResponse(Guid.NewGuid(), "user@example.com");
        // Expired the moment it was "issued" — simulates reopening the app long after the JWT's
        // own exp has passed.
        var auth = new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(-1));
        await new AuthSession(storage).SetSessionAsync(auth, user);

        var restored = new AuthSession(storage);
        await restored.RestoreAsync();

        Assert.False(restored.IsAuthenticated);
    }

    [Fact]
    public async Task ClearSession_RemovesEverythingFromStorage_SoARestoreAfterwardsFindsNothing()
    {
        var storage = new InMemorySecureStorageService();
        var user = new UserResponse(Guid.NewGuid(), "user@example.com");
        var auth = new AuthResponse("jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        var sut = new AuthSession(storage);
        await sut.SetSessionAsync(auth, user);

        sut.ClearSession();

        Assert.False(sut.IsAuthenticated);
        Assert.Null(sut.AccessToken);

        var restored = new AuthSession(storage);
        await restored.RestoreAsync();
        Assert.False(restored.IsAuthenticated);
    }

    [Fact]
    public async Task SetSessionAsync_RaisesAuthenticationStateChanged()
    {
        var sut = CreateSut(out _);
        var raised = false;
        sut.AuthenticationStateChanged += () => raised = true;

        await sut.SetSessionAsync(new AuthResponse("jwt", DateTimeOffset.UtcNow.AddHours(1)), new UserResponse(Guid.NewGuid(), "a@b.com"));

        Assert.True(raised);
    }

    [Fact]
    public void ClearSession_RaisesAuthenticationStateChanged()
    {
        var sut = CreateSut(out _);
        var raised = false;
        sut.AuthenticationStateChanged += () => raised = true;

        sut.ClearSession();

        Assert.True(raised);
    }
}
