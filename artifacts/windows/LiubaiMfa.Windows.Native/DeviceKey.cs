using System.Security.Cryptography;

namespace LiubaiMfa.Windows.Native;

public static class DeviceKey
{
    private static readonly byte[] Entropy = "LiubaiMFA:device-key:v1"u8.ToArray();
    public static byte[] Protect(byte[] key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
    }
    public static byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var key = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        if (key.Length != 32) { CryptographicOperations.ZeroMemory(key); throw new CryptographicException(); }
        return key;
    }
}
