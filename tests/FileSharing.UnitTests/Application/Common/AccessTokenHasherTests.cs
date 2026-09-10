using FileSharing.Application.Common;

namespace FileSharing.UnitTests.Application.Common;

public class AccessTokenHasherTests
{
    [Fact]
    public void Hash_IsDeterministic_ForTheSameToken()
    {
        var token = RandomTokenGenerator.Generate();

        Assert.Equal(AccessTokenHasher.Hash(token), AccessTokenHasher.Hash(token));
    }

    [Fact]
    public void Hash_DiffersFromTheOriginalToken()
    {
        var token = RandomTokenGenerator.Generate();

        Assert.NotEqual(token, AccessTokenHasher.Hash(token));
    }

    [Fact]
    public void Hash_DiffersBetweenDifferentTokens()
    {
        var first = RandomTokenGenerator.Generate();
        var second = RandomTokenGenerator.Generate();

        Assert.NotEqual(AccessTokenHasher.Hash(first), AccessTokenHasher.Hash(second));
    }

    [Fact]
    public void Hash_ProducesA64CharacterHexString_MatchingTheAccessTokenHashColumnLength()
    {
        var token = RandomTokenGenerator.Generate();

        var hash = AccessTokenHasher.Hash(token);

        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(Uri.IsHexDigit));
    }
}
