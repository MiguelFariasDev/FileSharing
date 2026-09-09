using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FileSharing.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the user id from the "sub" claim only — the caller must never be able to
    /// supply the id it wants to act as.
    /// </summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out userId);
    }
}
