using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Core;

public static class BackupCrypto
{
    private const string EncryptionPurpose = "mfa-verifier:encryption:v1";
    private const string AuthenticationPurpose = "mfa-verifier:authentication:v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string GenerateRecoveryKey()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    public static string NormalizeRecoveryKey(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    public static string FormatRecoveryKey(string value)
    {
        var normalized = NormalizeRecoveryKey(value);
        return string.Join(' ', Enumerable.Range(0, (normalized.Length + 3) / 4)
            .Select(index => normalized.Substring(index * 4, Math.Min(4, normalized.Length - index * 4))));
    }

    public static bool IsValidRecoveryKey(string value)
    {
        var normalized = NormalizeRecoveryKey(value);
        if (normalized.Length != 43 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return false;
        }

        try
        {
            return Base64UrlDecode(normalized).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static SecureEnvelope Encrypt(string plaintext, string keyMaterial)
    {
        return Encrypt(plaintext, keyMaterial, RandomNumberGenerator.GetBytes(16));
    }

    public static string Decrypt(SecureEnvelope envelope, string keyMaterial)
    {
        ValidateEnvelope(envelope);
        var (encryptionKey, authenticationKey) = DeriveKeys(keyMaterial);
        var expectedMac = ComputeMac(envelope, authenticationKey);
        var suppliedMac = Convert.FromHexString(envelope.Mac);
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, suppliedMac))
        {
            throw new CryptographicException("Encrypted payload authentication failed.");
        }

        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.IV = Convert.FromBase64String(envelope.Iv);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plaintext);
    }

    public static string CreateBackup(IReadOnlyCollection<MfaAccount> accounts, out string recoveryKey)
    {
        recoveryKey = GenerateRecoveryKey();
        var accountsJson = JsonSerializer.Serialize(accounts, JsonOptions);
        var payload = new SecureBackupPayload
        {
            Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = Encrypt(accountsJson, recoveryKey),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static IReadOnlyList<MfaAccount> ReadBackup(string backupJson, string recoveryKey)
    {
        if (Encoding.UTF8.GetByteCount(backupJson) > 5 * 1024 * 1024)
        {
            throw new FormatException("备份文件过大。");
        }

        if (!IsValidRecoveryKey(recoveryKey))
        {
            throw new FormatException("恢复密钥无效。");
        }

        var payload = JsonSerializer.Deserialize<SecureBackupPayload>(backupJson, JsonOptions)
            ?? throw new FormatException("备份内容无效。");
        if (payload.Version != 2 || !payload.Encrypted || payload.KeyFormat != "base64url-256")
        {
            throw new FormatException("仅支持 v2 加密备份。");
        }

        var accountsJson = Decrypt(payload.Data, NormalizeRecoveryKey(recoveryKey));
        var accounts = JsonSerializer.Deserialize<List<MfaAccount>>(accountsJson, JsonOptions)
            ?? throw new FormatException("备份账户内容无效。");
        if (accounts.Count > 10_000)
        {
            throw new FormatException("备份账户数量超过限制。");
        }

        return accounts.Select(account =>
        {
            var normalizedSecret = TotpService.NormalizeSecret(account.Secret);
            if (string.IsNullOrWhiteSpace(account.Id) ||
                string.IsNullOrWhiteSpace(account.Issuer) ||
                string.IsNullOrWhiteSpace(account.Account))
            {
                throw new FormatException("备份包含不完整的账户。");
            }

            _ = TotpService.Generate(normalizedSecret);
            return account with { Secret = normalizedSecret };
        }).ToList();
    }

    internal static SecureEnvelope Encrypt(string plaintext, string keyMaterial, byte[] iv)
    {
        if (iv.Length != 16)
        {
            throw new ArgumentException("IV must contain 16 bytes.", nameof(iv));
        }

        var (encryptionKey, authenticationKey) = DeriveKeys(keyMaterial);
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
        var envelope = new SecureEnvelope
        {
            Iv = Convert.ToBase64String(iv),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Mac = string.Empty,
        };
        return envelope with { Mac = Convert.ToHexString(ComputeMac(envelope, authenticationKey)).ToLowerInvariant() };
    }

    private static (byte[] EncryptionKey, byte[] AuthenticationKey) DeriveKeys(string keyMaterial)
    {
        var normalized = NormalizeRecoveryKey(keyMaterial);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new FormatException("密钥材料无效。");
        }

        var material = Encoding.UTF8.GetBytes(normalized);
        return (
            HMACSHA256.HashData(material, Encoding.UTF8.GetBytes(EncryptionPurpose)),
            HMACSHA256.HashData(material, Encoding.UTF8.GetBytes(AuthenticationPurpose)));
    }

    private static byte[] ComputeMac(SecureEnvelope envelope, byte[] authenticationKey)
    {
        var input = $"{envelope.Version}|{envelope.Algorithm}|{envelope.Iv}|{envelope.Ciphertext}";
        return HMACSHA256.HashData(authenticationKey, Encoding.UTF8.GetBytes(input));
    }

    private static void ValidateEnvelope(SecureEnvelope envelope)
    {
        if (envelope.Version != 1 ||
            envelope.Algorithm != SecureEnvelope.AlgorithmName ||
            string.IsNullOrWhiteSpace(envelope.Iv) ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext) ||
            string.IsNullOrWhiteSpace(envelope.Mac))
        {
            throw new FormatException("不支持的加密数据格式。");
        }

        if (Convert.FromBase64String(envelope.Iv).Length != 16 || Convert.FromHexString(envelope.Mac).Length != 32)
        {
            throw new FormatException("加密数据参数无效。");
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
