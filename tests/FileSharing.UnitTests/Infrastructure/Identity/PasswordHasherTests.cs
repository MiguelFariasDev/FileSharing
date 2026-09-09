using FileSharing.Infrastructure.Identity;

namespace FileSharing.UnitTests.Infrastructure.Identity;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void HashPassword_ReturnsHashDifferentFromOriginalPassword()
    {
        const string password = "SenhaForte123";

        var hash = _sut.HashPassword(password);

        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void VerifyPassword_ReturnsTrue_ForCorrectPassword()
    {
        const string password = "SenhaForte123";
        var hash = _sut.HashPassword(password);

        var result = _sut.VerifyPassword(hash, password);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_ReturnsFalse_ForIncorrectPassword()
    {
        const string password = "SenhaForte123";
        var hash = _sut.HashPassword(password);

        var result = _sut.VerifyPassword(hash, "SenhaErrada456");

        Assert.False(result);
    }
}
