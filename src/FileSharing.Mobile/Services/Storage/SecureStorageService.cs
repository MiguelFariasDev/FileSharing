using FileSharing.Mobile.Core.Services.Storage;

namespace FileSharing.Mobile.Services.Storage;

/// <summary>
/// The ONLY place in the whole solution that touches Microsoft.Maui.Storage.SecureStorage
/// (Android Keystore-backed) — everything else (AuthSession, ViewModels) depends on
/// ISecureStorageService instead. Holds the JWT and nothing else; never AWS credentials, never
/// a signing secret (see docs/mobile.md's security section — this app is never issued either).
/// </summary>
public class SecureStorageService : ISecureStorageService
{
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
