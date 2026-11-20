using System.Security.Cryptography;
using System.Text.Json;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Native;

namespace LiubaiMfa.CoreHost;

// 账户变更由会话 Gate 串行提交；独占文件锁同时限制其他宿主。
internal sealed class AccountStore(string directory) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string vaultDirectory = "";
    private string keyPath => Path.Combine(vaultDirectory, "device-key.v1.bin");
    private string accountPath => Path.Combine(vaultDirectory, "accounts.v2.json");
    private string initializedPath => Path.Combine(vaultDirectory, "initialized.v1");
    private FileStream? directoryLock;
    private bool failed;

    public List<MfaAccount> Load()
    {
        if (failed) throw new IOException("账户存储已停用。");
        try
        {
            Directory.CreateDirectory(directory);
            directoryLock ??= new FileStream(Path.Combine(directory, "electron.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ResolveVault();
            bool keyExists = File.Exists(keyPath), accountsExist = File.Exists(accountPath);
            if (Directory.Exists(keyPath) || Directory.Exists(accountPath) || keyExists != accountsExist) throw new IOException();
            if (!keyExists)
            {
                if (File.Exists(initializedPath) || Directory.Exists(initializedPath)) throw new IOException();
                return [];
            }
            if (new FileInfo(keyPath).Length > 16384 || new FileInfo(accountPath).Length > 5 * 1024 * 1024) throw new IOException();
            var key = DeviceKey.Unprotect(File.ReadAllBytes(keyPath));
            try
            {
                var envelope = JsonSerializer.Deserialize<SecureEnvelope>(File.ReadAllText(accountPath), JsonOptions) ?? throw new FormatException();
                var accounts = JsonSerializer.Deserialize<List<MfaAccount>>(BackupCrypto.Decrypt(envelope, Convert.ToBase64String(key)), JsonOptions) ?? throw new FormatException();
                if (accounts.Count > 1000 || accounts.Select(a => a.Id).Distinct().Count() != accounts.Count) throw new FormatException();
                foreach (var a in accounts)
                {
                    ValidateAccount(a);
                }
                if (accounts.Select(a => a.Secret).Distinct().Count() != accounts.Count) throw new FormatException();
                return accounts;
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (Exception error) { failed = true; throw new IOException("账户存储无法读取，已阻止访问。", error); }
    }

    public void Save(List<MfaAccount> accounts)
    {
        // 每次写入前重读验证，不将损坏或缺失文件误作空库。
        _ = Load();
        var exists = File.Exists(keyPath);
        var key = exists ? DeviceKey.Unprotect(File.ReadAllBytes(keyPath)) : RandomNumberGenerator.GetBytes(32);
        try
        {
            var envelope = BackupCrypto.Encrypt(JsonSerializer.Serialize(accounts, JsonOptions), Convert.ToBase64String(key));
            // 初次两文件不能跨文件原子提交；中断留下单文件时下次启动拒绝覆盖。
            if (!exists)
            {
                if (!File.Exists(Path.Combine(vaultDirectory,"protection.v1.json"))) WriteProtection(false);
                AtomicWrite(initializedPath, "initialized"u8.ToArray(), false);
                AtomicWrite(keyPath, DeviceKey.Protect(key), false);
            }
            AtomicWrite(accountPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions), exists);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public bool ReadProtection()
    {
        _ = Load();
        var settings = Path.Combine(vaultDirectory, "protection.v1.json");
        if (!File.Exists(settings)) {
            if (Directory.Exists(settings) || File.Exists(accountPath) || File.Exists(initializedPath) || File.Exists(Path.Combine(vaultDirectory,"protection.initialized.v1"))) throw new IOException();
            return false;
        }
        try {
            if (new FileInfo(settings).Length > 256) throw new IOException();
            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("enabled",out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new IOException();
            return enabled.GetBoolean();
        } catch { failed = true; throw new IOException("保护设置无法读取，已阻止访问。"); }
    }
    public void WriteProtection(bool enabled)
    {
        _ = Load();
        var settings = Path.Combine(vaultDirectory,"protection.v1.json");
        var marker = Path.Combine(vaultDirectory,"protection.initialized.v1");
        if (!File.Exists(marker)) AtomicWrite(marker,"initialized"u8.ToArray(),false);
        AtomicWrite(settings, JsonSerializer.SerializeToUtf8Bytes(new {enabled}), File.Exists(settings));
    }
    internal List<string> LoadGroups(List<MfaAccount>? accounts = null)
    {
        accounts ??= Load();
        try { return new GroupCatalog(vaultDirectory).Read(accounts); }
        catch (Exception error) { throw new IOException("无法读取分组记录，已阻止访问。", error); }
    }
    internal void SaveGroups(List<string> names)
    {
        var accounts = Load();
        var catalog = new GroupCatalog(vaultDirectory);
        catalog.Stage(accounts, accounts, names); catalog.Publish(names);
    }
    internal void SaveState(List<MfaAccount> accounts, List<string> names)
    {
        var before = Load();
        names = names.Concat(accounts.Select(account => account.Group ?? "").Where(name => name.Length > 0)).Distinct(StringComparer.Ordinal).ToList();
        var catalog = new GroupCatalog(vaultDirectory);
        if (names.Count == 0 && !catalog.Exists) { Save(accounts); return; }
        catalog.Stage(before, accounts, names);
        Save(accounts);
        catalog.Publish(names);
    }
    internal static void AtomicWrite(string destination, byte[] data, bool replace)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(data); stream.Flush(true); }
            if (replace) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => directoryLock?.Dispose();

    private void ResolveVault()
    {
        var migrated = Path.Combine(directory, "migrated-v1");
        var marker = Path.Combine(directory, "migration.initialized.v1");
        var hasVault = File.Exists(migrated) || Directory.Exists(migrated);
        var hasMarker = File.Exists(marker) || Directory.Exists(marker);
        if (!hasVault && !hasMarker) { vaultDirectory = directory; return; }
        EnsureNoReparse(directory);
        if (!Directory.Exists(migrated) || !File.Exists(marker) || Directory.Exists(marker) ||
            new[] { "device-key.v1.bin", "accounts.v2.json", "initialized.v1" }.Any(name => Exists(Path.Combine(directory, name)))) throw new IOException();
        EnsureNoReparse(migrated);
        foreach (var name in new[] { "device-key.v1.bin", "accounts.v2.json", "initialized.v1", "protection.v1.json", "protection.initialized.v1" })
        {
            var path = Path.Combine(migrated, name);
            if (!File.Exists(path) || Directory.Exists(path)) throw new IOException();
            EnsureNoReparse(path);
        }
        vaultDirectory = migrated;
    }

    internal bool MigrationEligible()
    {
        if (failed) return false;
        try {
            EnsureNoReparse(directory);
            return !new[] { "device-key.v1.bin", "accounts.v2.json", "initialized.v1", "migration.initialized.v1", "migrated-v1" }
                .Any(name => Exists(Path.Combine(directory, name)));
        } catch { return false; }
    }

    internal void PublishMigration(byte[] protectedKey, List<MfaAccount> accounts)
    {
        _ = Load();
        if (!MigrationEligible()) throw new MigrationOperationException("MIGRATION_TARGET_NOT_EMPTY", "当前账户库已初始化，无法迁移旧账户。");
        var stage = Path.Combine(directory, ".migration-" + Guid.NewGuid().ToString("N"));
        var marker = Path.Combine(directory, "migration.initialized.v1");
        var destination = Path.Combine(directory, "migrated-v1");
        bool markerWritten = false, published = false;
        try {
            Directory.CreateDirectory(stage);
            var key = DeviceKey.Unprotect(protectedKey);
            try {
                var envelope = BackupCrypto.Encrypt(JsonSerializer.Serialize(accounts, JsonOptions), Convert.ToBase64String(key));
                AtomicWrite(Path.Combine(stage, "device-key.v1.bin"), protectedKey, false);
                AtomicWrite(Path.Combine(stage, "accounts.v2.json"), JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions), false);
            } finally { CryptographicOperations.ZeroMemory(key); }
            AtomicWrite(Path.Combine(stage, "initialized.v1"), "initialized"u8.ToArray(), false);
            AtomicWrite(Path.Combine(stage, "protection.initialized.v1"), "initialized"u8.ToArray(), false);
            AtomicWrite(Path.Combine(stage, "protection.v1.json"), "{\"enabled\":true}"u8.ToArray(), false);
            using (var verification = new AccountStore(stage)) {
                if (!verification.Load().SequenceEqual(accounts) || !verification.ReadProtection()) throw new IOException();
            }
            if (!MigrationEligible()) throw new MigrationOperationException("MIGRATION_TARGET_NOT_EMPTY", "当前账户库已初始化，无法迁移旧账户。");
            AtomicWrite(marker, "initialized"u8.ToArray(), false); markerWritten = true;
            Directory.Move(stage, destination); published = true;
            vaultDirectory = destination;
        } catch (MigrationOperationException) { throw; }
        catch { throw new MigrationOperationException("MIGRATION_WRITE_FAILED", "无法保存迁移账户，请检查存储位置后重新预览。"); }
        finally {
            if (!published) {
                try {
                    EnsureNoReparse(directory); EnsureNoReparse(stage);
                    if (Directory.Exists(stage)) {
                        var children = Directory.GetFileSystemEntries(stage);
                        foreach (var child in children) { EnsureNoReparse(child); if (Directory.Exists(child)) throw new IOException(); }
                        foreach (var child in children) File.Delete(child);
                        Directory.Delete(stage);
                    }
                    if (markerWritten && !Exists(destination)) { EnsureNoReparse(marker); File.Delete(marker); }
                } catch { /* 清理不完整时保留标记，后续读取拒绝覆盖。 */ }
            }
        }
    }

    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    internal static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null) {
            if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            current = Path.GetDirectoryName(current);
        }
    }

    internal static bool IsSupportedColor(string? color) =>
        color is "blue" or "teal" or "green" or "amber" or "orange" or "red" or "pink" or "purple" or "slate";

    internal static void ValidateAccount(MfaAccount? account)
    {
        if (account is null || account.Secret != SecretValidation.Normalize(account.Secret) ||
            !ValidText(account.Id, 64) || !ValidText(account.Issuer, 80) || !ValidText(account.Account, 120) ||
            !ValidText(account.Group ?? "", 40, true) || !IsSupportedColor(account.Color))
            throw new FormatException();
    }

    private static bool ValidText(string? value, int maximum, bool empty = false) =>
        value is not null && value.Length <= maximum && (empty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);
}
