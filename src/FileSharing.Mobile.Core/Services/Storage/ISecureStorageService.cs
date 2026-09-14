namespace FileSharing.Mobile.Core.Services.Storage;

/// <summary>
/// Abstraction over the platform's secure credential store (Android Keystore-backed
/// SecureStorage) so ViewModels/AuthSession never depend on Microsoft.Maui.Storage directly —
/// the concrete implementation (FileSharing.Mobile.Services.Storage.SecureStorageService) is
/// the only place in the whole solution that touches it. Holds the JWT and nothing else: never
/// AWS credentials, never a signing secret — none of those are ever received by this app in the
/// first place (see docs/mobile.md).
/// </summary>
public interface ISecureStorageService
{
    Task SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
    void Remove(string key);
}
