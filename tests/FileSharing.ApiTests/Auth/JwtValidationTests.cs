using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FileSharing.ApiTests.Auth;

/// <summary>
/// Hand-crafts JWTs (rather than going through /api/auth/login) so each test can vary exactly
/// one validated property — issuer, audience, or signing key — while keeping everything else
/// (claims, lifetime) identical to a token the Api would have issued itself. Proves
/// AuthExtensions.AddJwtAuthentication's TokenValidationParameters actually reject each one,
/// not just that "some malformed string" is rejected (already covered by
/// AuthEndpointsTests.Me_WithInvalidToken_ReturnsUnauthorized).
/// </summary>
public class JwtValidationTests : IClassFixture<CustomWebApplicationFactory>
{
    // Matches CustomWebApplicationFactory's own override — a signature produced with this key
    // is otherwise indistinguishable from a real one issued by the Api under test.
    private const string CorrectSigningKey = CustomWebApplicationFactory.TestJwtSecretKey;
    private const string CorrectIssuer = "FileSharing.Tests";
    private const string CorrectAudience = "FileSharing.Api.Tests";

    private readonly HttpClient _client;

    public JwtValidationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string BuildToken(string signingKey, string issuer, string audience, DateTime? expires = null)
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var resolvedExpires = expires ?? DateTime.UtcNow.AddMinutes(30);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: resolvedExpires.AddMinutes(-31),
            expires: resolvedExpires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpStatusCode> CallMeWithAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Token_WithWrongIssuer_IsRejected()
    {
        var token = BuildToken(CorrectSigningKey, "some-other-issuer", CorrectAudience);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeWithAsync(token));
    }

    [Fact]
    public async Task Token_WithWrongAudience_IsRejected()
    {
        var token = BuildToken(CorrectSigningKey, CorrectIssuer, "some-other-audience");

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeWithAsync(token));
    }

    [Fact]
    public async Task Token_SignedWithAWrongKey_IsRejected()
    {
        // Same issuer/audience/claims/lifetime as a real token — only the signature differs,
        // proving ValidateIssuerSigningKey actually verifies the signature rather than trusting
        // whatever issuer/audience the token merely claims.
        var token = BuildToken("a-completely-different-signing-key-not-known-to-the-api-0123456789", CorrectIssuer, CorrectAudience);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeWithAsync(token));
    }

    [Fact]
    public async Task Token_Expired_IsRejected()
    {
        // Independent of AuthEndpointsTests' "garbage string" case: this is a structurally
        // valid, correctly-signed token whose only problem is exp already being in the past.
        var token = BuildToken(CorrectSigningKey, CorrectIssuer, CorrectAudience, expires: DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeWithAsync(token));
    }

    [Fact]
    public async Task Token_WithNoneAlgorithm_IsRejected()
    {
        // A classic JWT library attack: re-encode the header with "alg": "none" and drop the
        // signature entirely, hoping a lenient validator skips verification. TokenValidationParameters
        // never enables SignatureValidator overrides here, so this must still be rejected.
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = CorrectIssuer,
            Audience = CorrectAudience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = null
        };

        var unsignedToken = handler.CreateToken(descriptor);
        var unsignedJwt = handler.WriteToken(unsignedToken);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeWithAsync(unsignedJwt));
    }
}
