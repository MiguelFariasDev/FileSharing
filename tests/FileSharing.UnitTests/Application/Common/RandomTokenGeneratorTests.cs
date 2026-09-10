using FileSharing.Application.Common;

namespace FileSharing.UnitTests.Application.Common;

public class RandomTokenGeneratorTests
{
    [Fact]
    public void Generate_DefaultLength_HasAtLeast128BitsOfEntropy()
    {
        // Default is 32 raw bytes (256 bits) base64url-encoded, well above the 128-bit /
        // 22+ base62-character floor required for a public access token.
        var token = RandomTokenGenerator.Generate();

        Assert.True(token.Length >= 22, $"Expected at least 22 characters, got {token.Length}.");
    }

    [Fact]
    public void Generate_ProducesUrlSafeCharactersOnly()
    {
        var token = RandomTokenGenerator.Generate();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Generate_ProducesDifferentTokens_OnEachCall()
    {
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => RandomTokenGenerator.Generate())
            .ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }
}
