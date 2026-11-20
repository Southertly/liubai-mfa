using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;

namespace LiubaiMfa.CoreHost;

internal sealed class BackupService : IDisposable
{
    private const int MaximumBytes = 5 * 1024 * 1024;
    private readonly AccountStore store;
    private readonly ProtectionSession session;
    private readonly TimeSpan lifetime;
    private readonly Timer expiryTimer;
    private PendingBackup? pending;
    private long operationEpoch;
    private bool disposed;
    private sealed record PendingBackup(string Token, long Generation, long Deadline, byte[]? Export,
        List<MfaAccount>? Imports, byte[]? Baseline, int Skipped, List<string>? Groups = null);

    public BackupService(AccountStore store, ProtectionSession session, TimeSpan? lifetime = null)
    {
        this.store = store; this.session = session; this.lifetime = lifetime ?? TimeSpan.FromSeconds(120);
        expiryTimer = new Timer(_ => { lock (session.Gate) { if (pending is not null && Expired(pending)) Clear(); } });
        session.Invalidated += Cancel;
    }

    public async Task<object> PrepareExportAsync(JsonElement payload)
    {
        long epoch;
        Task<bool> authentication;
        lock (session.Gate)
        {
            Exact(payload);
            RequireUnlocked();
            Cancel(); epoch = operationEpoch;
            authentication = session.VerifyAsync(null);
        }
        var verified = await authentication;
        lock (session.Gate)
        {
            if (disposed || epoch != operationEpoch) throw ExpiredError();
            if (!verified) throw new BackupOperationException("AUTH_CANCELLED", "身份验证未完成，请重试。");
            RequireUnlocked();
            var accounts = store.Load();
            var backup = JsonNode.Parse(BackupCrypto.CreateBackup(accounts, out var recoveryKey))!.AsObject();
            backup["groupData"] = JsonSerializer.SerializeToNode(BackupCrypto.Encrypt(JsonSerializer.Serialize(store.LoadGroups(accounts)), recoveryKey));
            var data = Encoding.UTF8.GetBytes(backup.ToJsonString());
            if (data.Length > MaximumBytes) throw new BackupOperationException("BACKUP_TOO_LARGE", "备份内容超过大小限制。");
            var token = Guid.NewGuid().ToString("N");
            SetPending(new(token, session.Generation, Deadline(), data, null, null, 0));
            return new { token, recoveryKey };
        }
    }

    public object Handle(string method, JsonElement payload)
    {
        lock (session.Gate)
        {
            if (disposed) throw ExpiredError();
            if (method == "backupCancel") { Exact(payload); Cancel(); return new { cancelled = true }; }
            RequireUnlocked();
            return method switch
            {
                "exportSave" => SaveExport(payload),
                "importPreview" => PreviewImport(payload),
                "importCommit" => CommitImport(payload),
                _ => throw new ArgumentException("输入内容无效，请检查各字段。")
            };
        }
    }

    private object SaveExport(JsonElement payload)
    {
        Exact(payload, "token", "path", "confirmed");
        var token = Text(payload, "token", 64);
        var path = FilePath(payload);
        if (payload.GetProperty("confirmed").ValueKind != JsonValueKind.True)
            throw new ArgumentException("请先确认已保存恢复密钥。");
        var state = GetPending(token, export: true);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(state.Export!); stream.Flush(true); }
            File.Move(temporary, path, false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new BackupOperationException("BACKUP_WRITE_FAILED", "无法保存备份，请选择新的文件名或其他位置。"); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Clear();
        return new { saved = true };
    }

    private object PreviewImport(JsonElement payload)
    {
        Exact(payload, "path", "recoveryKey");
        var path = FilePath(payload);
        var recoveryKey = Text(payload, "recoveryKey", 256);
        Cancel();
        List<MfaAccount> imported;
        List<string> importedGroups = [];
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumBytes) throw new FormatException();
            using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = file.Read(buffer)) != 0)
            {
                if (bytes.Length + count > MaximumBytes) throw new FormatException();
                bytes.Write(buffer, 0, count);
            }
            var json = new UTF8Encoding(false, true).GetString(bytes.ToArray());
            imported = BackupCrypto.ReadBackup(json, recoveryKey).ToList();
            if (imported.Count > 1000) throw new FormatException();
            // ReadBackup 兼容旧版的宽松规范化；原始密钥另作严格校验，防止错误填充被吞掉。
            using var envelope = JsonDocument.Parse(json);
            if (envelope.RootElement.TryGetProperty("groupData", out var groupData))
            {
                var encryptedGroups = groupData.Deserialize<SecureEnvelope>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                importedGroups = JsonSerializer.Deserialize<List<string>>(BackupCrypto.Decrypt(encryptedGroups, recoveryKey)) ?? throw new FormatException();
                GroupCatalog.Validate(importedGroups);
            }
            var data = envelope.RootElement.GetProperty("data").Deserialize<SecureEnvelope>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            using var raw = JsonDocument.Parse(BackupCrypto.Decrypt(data, recoveryKey));
            for (var index = 0; index < imported.Count; index++)
            {
                var secret = SecretValidation.Normalize(raw.RootElement[index].GetProperty("secret").GetString()!);
                imported[index] = imported[index] with { Secret = secret, Color = imported[index].Color ?? "blue" };
                AccountStore.ValidateAccount(imported[index]);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or CryptographicException or JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
        { throw new BackupOperationException("BACKUP_INVALID", "无法读取备份，请检查文件和恢复密钥。"); }
        // 存储读取异常必须继续上抛，不能把损坏账户库当成空库。
        var existing = store.Load();
        var secrets = existing.Select(a => a.Secret).ToHashSet(StringComparer.Ordinal);
        var ids = existing.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var additions = new List<MfaAccount>();
        var skipped = 0;
        foreach (var account in imported)
        {
            if (!secrets.Add(account.Secret)) { skipped++; continue; }
            var id = account.Id;
            if (!ids.Add(id)) { do { id = Guid.NewGuid().ToString("N"); } while (!ids.Add(id)); }
            additions.Add(account with { Id = id });
        }
        var currentGroups = store.LoadGroups(existing);
        var mergedGroups = currentGroups.Concat(importedGroups).Concat(additions.Select(account => account.Group ?? "").Where(name => name.Length > 0)).Distinct(StringComparer.Ordinal).ToList();
        CheckCapacity([.. existing, .. additions], mergedGroups);
        var token = Guid.NewGuid().ToString("N");
        SetPending(new(token, session.Generation, Deadline(), null, additions, Fingerprint(existing), skipped, mergedGroups));
        return new { token, added = additions.Count, skipped, groupsAdded = mergedGroups.Count - currentGroups.Count, accounts = additions.Select(a => new { issuer = a.Issuer, account = a.Account, group = a.Group ?? "" }).ToArray() };
    }

    private object CommitImport(JsonElement payload)
    {
        Exact(payload, "token");
        var state = GetPending(Text(payload, "token", 64), export: false);
        var current = store.Load();
        if (!CryptographicOperations.FixedTimeEquals(Fingerprint(current), state.Baseline!))
        { Clear(); throw new BackupOperationException("BACKUP_CHANGED", "账户已发生变化，请重新预览备份。"); }
        var merged = current.Concat(state.Imports!).ToList();
        CheckCapacity(merged, state.Groups);
        var groupsAdded = state.Groups!.Count - store.LoadGroups(current).Count;
        if (state.Imports!.Count > 0) store.SaveState(merged, state.Groups);
        else if (groupsAdded > 0) store.SaveGroups(state.Groups);
        var result = new { added = state.Imports.Count, skipped = state.Skipped, groupsAdded };
        Clear(); return result;
    }

    private static void CheckCapacity(List<MfaAccount> accounts, List<string>? groups = null)
    {
        try { if(groups is not null)GroupCatalog.Validate(groups); AccountService.CheckCapacity(accounts, groups); }
        catch (Exception error) when (error is ArgumentException or FormatException) { throw new BackupOperationException("BACKUP_TOO_LARGE", "合并后账户或分组数量、内容超过限制。"); }
    }
    private PendingBackup GetPending(string token, bool export)
    {
        if (pending is null) throw ExpiredError();
        if (Expired(pending) || pending.Generation != session.Generation) { Clear(); throw ExpiredError(); }
        if (pending.Token != token || (pending.Export is not null) != export) throw ExpiredError();
        return pending;
    }
    private void RequireUnlocked()
    {
        if (session.Pending) throw new BackupOperationException("AUTH_PENDING", "身份验证正在进行。");
        if (session.Locked) throw new BackupOperationException("LOCKED", "账户已锁定，请先验证身份。");
    }
    private long Deadline() => Environment.TickCount64 + (long)lifetime.TotalMilliseconds;
    private static bool Expired(PendingBackup state) => Environment.TickCount64 >= state.Deadline;
    private void SetPending(PendingBackup value) { Clear(); pending = value; expiryTimer.Change(lifetime, Timeout.InfiniteTimeSpan); }
    private void Clear() { pending = null; expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
    public void Cancel() { lock (session.Gate) { operationEpoch++; Clear(); } }
    public void Dispose() { lock (session.Gate) { if (disposed) return; Cancel(); disposed = true; session.Invalidated -= Cancel; expiryTimer.Dispose(); } }
    private byte[] Fingerprint(List<MfaAccount> accounts) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { accounts, groups = store.LoadGroups(accounts) }));
    private static BackupOperationException ExpiredError() => new("BACKUP_EXPIRED", "备份操作已失效，请重新开始。");
    private static void Exact(JsonElement payload, params string[] required)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("输入内容无效，请检查各字段。");
        var fields = payload.EnumerateObject().ToArray();
        if (fields.Length != required.Length || fields.Select(f => f.Name).Distinct().Count() != fields.Length || required.Any(f => !payload.TryGetProperty(f, out _)))
            throw new ArgumentException("输入内容无效，请检查各字段。");
    }
    private static string Text(JsonElement payload, string field, int maximum)
    {
        var value = payload.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum || value.GetString()!.Any(char.IsControl))
            throw new ArgumentException("输入内容无效，请检查各字段。");
        return value.GetString()!;
    }
    private static string FilePath(JsonElement payload)
    {
        var path = Text(payload, "path", 32767);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("请选择有效的文件路径。");
        return path;
    }
}
