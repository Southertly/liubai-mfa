using System.Security.Cryptography;

namespace LiubaiMfa.Windows.Services;

public sealed class WindowsSecureKeyStore
{
    private static readonly byte[] Entropy = "LiubaiMFA:device-key:v1"u8.ToArray();
    private readonly string _keyPath;

    public WindowsSecureKeyStore(string dataDirectory)
    {
        _keyPath = Path.Combine(dataDirectory, "device-key.v1.bin");
    }

    public async Task<byte[]> GetOrCreateAsync()
    {
        if (File.Exists(_keyPath))
        {
            var protectedKey = await File.ReadAllBytesAsync(_keyPath);
            return ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        await File.WriteAllBytesAsync(_keyPath, protectedBytes);
        return key;
    }
}
