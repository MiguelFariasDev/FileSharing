using System.Text.Json;

namespace FileSharing.Mobile.Tests.Security;

/// <summary>
/// Automated regression guard for Fase 13 §31's security audit — scans the actual source tree
/// (never build output, never a person's home directory/environment) for patterns that should
/// never legitimately appear in this app's own code, matching the specific list this phase calls
/// out: AWS credentials, a JWT signing secret, a private key, or a real database connection
/// string. Patterns are deliberately specific (an AWS access key ID's own fixed prefix, a PEM
/// header, etc.) rather than a bare word like "password", to avoid false positives against
/// perfectly legitimate code such as an Entry's IsPassword="True" binding.
/// </summary>
public class MobileSecretsScanTests
{
    private static readonly string[] ForbiddenPatterns =
    [
        "AKIA", // AWS access key id prefix
        "aws_secret_access_key",
        "aws_session_token",
        "BEGIN RSA PRIVATE KEY",
        "BEGIN OPENSSH PRIVATE KEY",
        "BEGIN EC PRIVATE KEY",
        "jwt_secret",
    ];

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileSharing.slnx")))
                dir = dir.Parent;

            return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (FileSharing.slnx) from the test's output directory.");
        }
    }

    private static IEnumerable<string> MobileSourceFiles()
    {
        foreach (var project in new[] { "src/FileSharing.Mobile", "src/FileSharing.Mobile.Core" })
        {
            var projectDir = Path.Combine(RepoRoot, project);
            if (!Directory.Exists(projectDir))
                continue;

            foreach (var extension in new[] { "*.cs", "*.xaml", "*.json" })
            {
                foreach (var file in Directory.EnumerateFiles(projectDir, extension, SearchOption.AllDirectories))
                {
                    // bin/obj are build output, not source — irrelevant to a source-tree audit
                    // and would otherwise force scanning generated/copied files twice.
                    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        continue;

                    yield return file;
                }
            }
        }
    }

    [Fact]
    public void NoMobileSourceFile_ContainsAKnownSecretPattern()
    {
        var offenders = new List<string>();

        foreach (var file in MobileSourceFiles())
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in ForbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)} (pattern: {pattern})");
            }
        }

        Assert.True(offenders.Count == 0, "Possible secret pattern(s) found:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void MobileAppSettings_OnlyEverContainsTheApiBaseUrlAndHubUrl_NeverAnySecretLookingKey()
    {
        var path = Path.Combine(RepoRoot, "src/FileSharing.Mobile/Resources/Raw/appsettings.json");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var api = document.RootElement.GetProperty("Api");

        var propertyNames = api.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["BaseUrl", "HubUrl"], propertyNames);

        // The API base URL is explicitly not a secret (Fase 13 §29) — but it should still never
        // accidentally be a full connection-string-shaped value or contain a credential.
        foreach (var property in api.EnumerateObject())
        {
            var value = property.Value.GetString() ?? string.Empty;
            Assert.DoesNotContain("@", value); // no embedded user:password@host
            Assert.DoesNotContain("Password=", value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
