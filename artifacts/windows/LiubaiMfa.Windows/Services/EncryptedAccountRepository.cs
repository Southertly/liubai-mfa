using System.Security.Cryptography;
using System.Text.Json;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.Windows.Services;

public sealed class EncryptedAccountRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly string _accountPath;
    private readonly WindowsSecureKeyStore _keyStore;

    public EncryptedAccountRepository(string dataDirectory)
    {
        _accountPath = Path.Combine(dataDirectory, "accounts.v2.json");
        _keyStore = new WindowsSecureKeyStore(dataDirectory);
    }

    public async Task<IReadOnlyList<MfaAccount>> LoadAsync()
    {
        var key = await _keyStore.GetOrCreateAsync();
        if (!File.Exists(_accountPath))
        {
            return Array.Empty<MfaAccount>();
        }

        var serializedEnvelope = await File.ReadAllTextAsync(_accountPath);
        var envelope = JsonSerializer.Deserialize<SecureEnvelope>(serializedEnvelope, JsonOptions)
            ?? throw new CryptographicException("本地账户数据格式无效。");
        var accountJson = BackupCrypto.Decrypt(envelope, Convert.ToBase64String(key));
        return JsonSerializer.Deserialize<List<MfaAccount>>(accountJson, JsonOptions)
            ?? throw new CryptographicException("本地账户数据无法解锁。");
    }

    public async Task SaveAsync(IReadOnlyCollection<MfaAccount> accounts)
    {
        var key = await _keyStore.GetOrCreateAsync();
        var accountJson = JsonSerializer.Serialize(accounts, JsonOptions);
        var envelope = BackupCrypto.Encrypt(accountJson, Convert.ToBase64String(key));
        var serializedEnvelope = JsonSerializer.Serialize(envelope, JsonOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(_accountPath)!);
        var temporaryPath = _accountPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, serializedEnvelope);
        File.Move(temporaryPath, _accountPath, true);
    }
}
