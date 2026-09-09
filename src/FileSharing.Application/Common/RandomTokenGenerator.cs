using System.Security.Cryptography;

namespace FileSharing.Application.Common;

/// <summary>
/// Generates non-predictable, high-entropy opaque tokens (e.g. S3 storage keys) using a
/// cryptographically secure random generator — never a sequential id or a value derived
/// from user input such as the original file name.
/// </summary>
public static class RandomTokenGenerator
{
    public static string Generate(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);

        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
