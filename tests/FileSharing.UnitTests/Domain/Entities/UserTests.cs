using FileSharing.Domain.Entities;

namespace FileSharing.UnitTests.Domain.Entities;

public class UserTests
{
    [Fact]
    public void ChangePasswordHash_ReplacesThePreviousHash()
    {
        var user = new User("user@example.com", "old-hash");

        user.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePasswordHash_Throws_WhenHashIsEmpty()
    {
        var user = new User("user@example.com", "old-hash");

        Assert.Throws<ArgumentException>(() => user.ChangePasswordHash("   "));
    }
}
