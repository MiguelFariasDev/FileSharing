using FileSharing.Mobile.Core.Services.Storage;

namespace FileSharing.Mobile.Tests.Services;

/// <summary>Stands in for the real Android-Keystore-backed SecureStorageService in tests — same
/// contract (ISecureStorageService), a plain in-memory dictionary instead of the OS keystore.</summary>
public class InMemorySecureStorageService : ISecureStorageService
{
    private readonly Dictionary<string, string> _values = [];

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));

    public void Remove(string key) => _values.Remove(key);
}
