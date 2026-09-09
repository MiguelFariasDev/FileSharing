using System.IdentityModel.Tokens.Jwt;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Domain.Entities;
using FileSharing.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FileSharing.UnitTests.Infrastructure.Identity;

public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateGenerator(int expirationMinutes = 60)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "FileSharing.Tests",
            Audience = "FileSharing.Api.Tests",
            SecretKey = "unit-test-signing-key-with-enough-entropy-0123456789",
            ExpirationMinutes = expirationMinutes
        });

        return new JwtTokenGenerator(options);
    }

    private static User CreateUser() => new("user@example.com", "irrelevant-hash");

    [Fact]
    public void GenerateToken_ReturnsNonEmptyAccessToken()
    {
        var generator = CreateGenerator();
        var user = CreateUser();

        var (accessToken, _) = generator.GenerateToken(user);

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
    }

    [Fact]
    public void GenerateToken_TokenContainsExpectedClaims()
    {
        var generator = CreateGenerator();
        var user = CreateUser();

        var (accessToken, _) = generator.GenerateToken(user);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Equal(user.Id.ToString(), token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Contains(token.Claims, c => c.Type == JwtRegisteredClaimNames.Jti && !string.IsNullOrWhiteSpace(c.Value));
        Assert.Contains(token.Claims, c => c.Type == JwtRegisteredClaimNames.Iat);
        Assert.NotEqual(default, token.ValidTo);
    }

    [Fact]
    public void GenerateToken_ExpiresAtMatchesConfiguredExpirationMinutes()
    {
        const int expirationMinutes = 15;
        var generator = CreateGenerator(expirationMinutes);
        var user = CreateUser();

        var before = DateTimeOffset.UtcNow;
        var (accessToken, expiresAt) = generator.GenerateToken(user);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(expiresAt, before.AddMinutes(expirationMinutes), after.AddMinutes(expirationMinutes));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(expiresAt.UtcDateTime, token.ValidTo, TimeSpan.FromSeconds(1));
    }
}
