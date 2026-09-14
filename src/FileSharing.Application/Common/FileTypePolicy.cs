namespace FileSharing.Application.Common;

/// <summary>
/// Centralized, explicit allowlist of the file types this version of the platform accepts.
/// Deliberately small and easy to extend — no executables/scripts, no "match everything" rule.
/// </summary>
public static class FileTypePolicy
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensionsByContentType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Documentos
            ["application/pdf"] = [".pdf"],
            ["application/epub+zip"] = [".epub"],

            // Imagens
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"],
            ["image/webp"] = [".webp"],
            ["image/gif"] = [".gif"],

            // Vídeos
            ["video/mp4"] = [".mp4"],
            ["video/webm"] = [".webm"],
            ["video/quicktime"] = [".mov"],
            ["video/x-matroska"] = [".mkv"],

            // Áudio
            ["audio/mpeg"] = [".mp3"],
            ["audio/wav"] = [".wav"],
            ["audio/x-wav"] = [".wav"],
            ["audio/ogg"] = [".ogg"],
            ["audio/mp4"] = [".m4a"],
            ["audio/aac"] = [".aac"],
            ["audio/flac"] = [".flac"],

            // Pastas compactadas
            ["application/zip"] = [".zip"],
        };

    public static bool IsContentTypeAllowed(string contentType) =>
        AllowedExtensionsByContentType.ContainsKey(contentType);

    public static bool IsExtensionAllowedForContentType(string contentType, string extension)
    {
        if (!AllowedExtensionsByContentType.TryGetValue(contentType, out var extensions))
            return false;

        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the file name has an extension consistent with the declared content type.
    /// Rejects mismatches such as "document.exe" declared as "application/pdf".
    /// </summary>
    public static bool IsFileNameConsistentWithContentType(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && IsExtensionAllowedForContentType(contentType, extension);
    }

    /// <summary>
    /// True when the original file name is safe to store as plain metadata. The name is never
    /// used to build a filesystem/S3 path (<c>File.StorageKey</c> is always a server-generated
    /// <see cref="RandomTokenGenerator"/> value — see FileUploadService), so a path-traversal
    /// sequence in it cannot actually reach storage today; this rejects it anyway, as defense in
    /// depth against any future code path that might reuse the original name more directly, and
    /// because a name like "../../etc/passwd.pdf" has no legitimate reason to exist regardless.
    /// </summary>
    public static bool IsFileNameSafe(string fileName) =>
        !fileName.Contains("..", StringComparison.Ordinal) &&
        !fileName.Contains('/') &&
        !fileName.Contains('\\') &&
        fileName.All(c => !char.IsControl(c));
}
