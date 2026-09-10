using System.Security.Cryptography;
using System.Text;

namespace FileSharing.Application.Common;

/// <summary>
/// Deterministic hash used to look up a <c>File</c> by its public access token without ever
/// persisting the token itself. SHA-256 (not a salted/slow password hash) is required here
/// on purpose: the same plaintext token must always produce the same hash so it can be found
/// by an equality lookup, and the token already carries 256 bits of its own entropy from
/// <see cref="RandomTokenGenerator"/>, so brute-forcing the hash is not a realistic concern.
/// </summary>
public static class AccessTokenHasher
{
    public static string Hash(string accessToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        return Convert.ToHexString(bytes);
    }
}
