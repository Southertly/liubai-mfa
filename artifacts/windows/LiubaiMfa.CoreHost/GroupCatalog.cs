using System.Security.Cryptography;
using System.Text.Json;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.CoreHost;

// 提交记录同时保留前后快照；账户文件的原子替换决定使用哪份分组，读取不写盘。
internal sealed class GroupCatalog(string directory)
{
    private static readonly byte[] Entropy = "LiubaiMFA:groups:v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string CatalogPath => Path.Combine(directory, "groups.v1.bin");
    private string PendingPath => Path.Combine(directory, "groups.pending.v1.bin");
    private string MarkerPath => Path.Combine(directory, "groups.initialized.v1");
    internal sealed record Snapshot(int Version, List<string> Names);
    internal sealed record Pending(int Version, string BeforeHash, string AfterHash, List<string> Before, List<string> After);
    internal bool Exists => File.Exists(CatalogPath) || File.Exists(PendingPath) || File.Exists(MarkerPath) || Directory.Exists(CatalogPath);

    internal List<string> Read(List<MfaAccount> accounts)
    {
        List<string> stored;
        if (File.Exists(PendingPath))
        {
            var pending = ReadProtected<Pending>(PendingPath);
            Validate(pending.Before); Validate(pending.After);
            if (pending.Version != 1 || pending.BeforeHash.Length != 64 || pending.AfterHash.Length != 64) throw new IOException("分组提交记录无效。");
            var current = Fingerprint(accounts);
            stored = current == pending.AfterHash ? pending.After : current == pending.BeforeHash ? pending.Before : throw new IOException("分组与账户状态不一致。");
        }
        else if (File.Exists(CatalogPath))
        {
            var snapshot = ReadProtected<Snapshot>(CatalogPath);
            if (snapshot.Version != 1) throw new IOException("分组版本无效。");
            stored = snapshot.Names;
        }
        else
        {
            if (File.Exists(MarkerPath) || Directory.Exists(CatalogPath) || Directory.Exists(PendingPath) || Directory.Exists(MarkerPath)) throw new IOException("分组记录缺失。");
            stored = [];
        }
        Validate(stored);
        var names = stored.Concat(accounts.Select(account => account.Group ?? "").Where(name => name.Length > 0)).Distinct(StringComparer.Ordinal).ToList();
        Validate(names); return names;
    }

    internal void Stage(List<MfaAccount> before, List<MfaAccount> after, List<string> names)
    {
        Validate(names);
        var oldNames = Read(before);
        WriteProtected(PendingPath, new Pending(1, Fingerprint(before), Fingerprint(after), oldNames, names));
    }

    internal void Publish(List<string> names)
    {
        // 提交记录已持久化；清理失败时继续以其为准，不丢失已提交的分组。
        try { WriteProtected(CatalogPath, new Snapshot(1, names)); if (!File.Exists(MarkerPath)) AccountStore.AtomicWrite(MarkerPath, "initialized"u8.ToArray(), false); File.Delete(PendingPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Validate(List<string>? names)
    {
        if (names is null || names.Count > 1000 || names.Distinct(StringComparer.Ordinal).Count() != names.Count || names.Any(name => name is null || name.Length is 0 or > 40 || name.Any(char.IsControl))) throw new FormatException("分组记录无效。");
    }
    private static string Fingerprint(List<MfaAccount> accounts)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(accounts, JsonOptions);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static T ReadProtected<T>(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 512 * 1024) throw new IOException();
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<T>(clear, JsonOptions) ?? throw new IOException("分组记录无效。"); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void WriteProtected<T>(string path, T value)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try { AccountStore.AtomicWrite(path, ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser), File.Exists(path)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
