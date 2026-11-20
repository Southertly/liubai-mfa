using System.Security.Cryptography;
using System.Text.Json;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Native;

namespace LiubaiMfa.CoreHost;

// 源目录始终只读；草稿仅保存指纹和展示信息，提交时重读并持有文件句柄至发布完成。
internal sealed class MigrationService : IDisposable
{
    private readonly AccountStore store;
    private readonly ProtectionSession session;
    private readonly TimeSpan lifetime;
    private readonly Timer timer;
    private PendingMigration? pending;
    private long epoch;
    private bool disposed;
    private sealed record PendingMigration(string Token, string Directory, byte[] Fingerprint, long Generation, long Deadline);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public MigrationService(AccountStore store, ProtectionSession session, TimeSpan? lifetime = null)
    {
        this.store = store; this.session = session; this.lifetime = lifetime ?? TimeSpan.FromSeconds(120);
        timer = new Timer(_ => { lock(session.Gate) { if (pending is not null && Environment.TickCount64 >= pending.Deadline) Cancel(); } });
        session.Invalidated += Cancel;
    }

    public async Task<object> PreviewAsync(JsonElement payload)
    {
        var operation = StartPreview(payload);
        var verified = await operation.Authentication;
        return FinishPreview(operation.Ticket, operation.Source, verified);
    }

    private (long Ticket, string Source, Task<bool> Authentication) StartPreview(JsonElement payload)
    {
        lock(session.Gate) {
            Exact(payload, "sourceDirectory");
            var field = payload.GetProperty("sourceDirectory");
            if (field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString()) ||
                field.GetString()!.Length > 32767 || field.GetString()!.Any(char.IsControl) || !Path.IsPathFullyQualified(field.GetString()!)) throw Invalid();
            var source = Path.GetFullPath(field.GetString()!);
            RequireUnlocked(); RequireEligible(); Cancel();
            return (epoch, source, session.VerifyAsync(null));
        }
    }

    private object FinishPreview(long ticket, string source, bool verified)
    {
        lock(session.Gate) {
            if (disposed || ticket != epoch) throw Expired();
            if (!verified) throw new MigrationOperationException("AUTH_CANCELLED", "身份验证未完成，请重试。");
            RequireUnlocked(); RequireEligible();
            using var snapshot = Snapshot.Open(source);
            var token = Guid.NewGuid().ToString("N");
            pending = new(token, source, snapshot.Fingerprint.ToArray(), session.Generation, Environment.TickCount64 + (long)lifetime.TotalMilliseconds);
            timer.Change(lifetime, Timeout.InfiniteTimeSpan);
            return new { token, count = snapshot.Accounts.Count,
                accounts = snapshot.Accounts.Select(a => new { issuer = a.Issuer, account = a.Account, group = a.Group ?? "" }).ToArray(), protectionAfterMigration = true };
        }
    }

    public object Handle(string method, JsonElement payload)
    {
        lock(session.Gate) {
            if (disposed) throw Expired();
            if (method == "migrationCancel") { Exact(payload); Cancel(); return new { cancelled = true }; }
            if (method != "migrationCommit") throw Invalid();
            Exact(payload, "token", "confirmed");
            if (payload.GetProperty("confirmed").ValueKind != JsonValueKind.True || payload.GetProperty("token").ValueKind != JsonValueKind.String) throw Invalid();
            var state = pending;
            if (state is null || Environment.TickCount64 >= state.Deadline || state.Generation != session.Generation || state.Token != payload.GetProperty("token").GetString()) { Cancel(); throw Expired(); }
            RequireUnlocked();
            try {
                RequireEligible();
                using var snapshot = Snapshot.Open(state.Directory, state.Fingerprint);
                store.PublishMigration(snapshot.ProtectedKey, snapshot.Accounts);
                var count = snapshot.Accounts.Count;
                session.ApplyMigrationProtection();
                return new { migrated = true, count, protectionEnabled = true, locked = false };
            } finally { Cancel(); }
        }
    }

    private void RequireEligible() { if (!store.MigrationEligible()) throw new MigrationOperationException("MIGRATION_TARGET_NOT_EMPTY", "当前账户库已初始化，无法迁移旧账户。"); }
    private void RequireUnlocked() {
        if (disposed) throw Expired();
        if (session.Pending) throw new MigrationOperationException("AUTH_PENDING", "身份验证正在进行。");
        if (session.Locked) throw new MigrationOperationException("LOCKED", "账户已锁定，请先验证身份。");
    }
    public void Cancel() { lock(session.Gate) { epoch++; if (pending is not null) CryptographicOperations.ZeroMemory(pending.Fingerprint); pending = null; timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } }
    public void Dispose() { lock(session.Gate) { if (disposed) return; Cancel(); disposed = true; session.Invalidated -= Cancel; timer.Dispose(); } }
    private static void Exact(JsonElement payload, params string[] fields) {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid();
        var actual = payload.EnumerateObject().ToArray();
        if (actual.Length != fields.Length || actual.Select(p => p.Name).Distinct().Count() != actual.Length || fields.Any(f => !payload.TryGetProperty(f, out _))) throw Invalid();
    }
    private static MigrationOperationException Invalid() => new("MIGRATION_INVALID", "无法读取旧账户，请检查文件并关闭旧版后重试。");
    private static MigrationOperationException Expired() => new("MIGRATION_EXPIRED", "迁移操作已失效，请重新预览。");
    private static MigrationOperationException Unsupported() => new("MIGRATION_UNSUPPORTED", "旧账户的字段、颜色或重复记录超出新版支持范围，请先在旧版核对后重试。原文件未更改。");

    private sealed class Snapshot : IDisposable
    {
        private FileStream? keyFile, accountFile;
        public byte[] ProtectedKey { get; private set; } = [];
        private byte[] ciphertext = [];
        public byte[] Fingerprint { get; private set; } = [];
        public List<MfaAccount> Accounts { get; private set; } = [];
        public static Snapshot Open(string directory, byte[]? expectedFingerprint = null) {
            var result = new Snapshot();
            try {
                AccountStore.EnsureNoReparse(directory);
                if (!Directory.Exists(directory)) throw Invalid();
                var key = Path.Combine(directory, "device-key.v1.bin"); var accounts = Path.Combine(directory, "accounts.v2.json");
                if (AccountStore.Exists(Path.Combine(directory, "accounts.v2.json.tmp"))) throw new MigrationOperationException("MIGRATION_BUSY", "旧版可能正在写入，请关闭旧版后重试。");
                foreach (var path in new[] { key, accounts }) { AccountStore.EnsureNoReparse(path); if (!File.Exists(path) || Directory.Exists(path)) throw Invalid(); }
                try {
                    result.keyFile = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.Read);
                    result.accountFile = new FileStream(accounts, FileMode.Open, FileAccess.Read, FileShare.Read);
                } catch (IOException) { throw new MigrationOperationException("MIGRATION_BUSY", "旧账户正在使用中，请关闭旧版后重试。"); }
                result.ProtectedKey = ReadBounded(result.keyFile, 16384);
                result.ciphertext = ReadBounded(result.accountFile, 5 * 1024 * 1024);
                AccountStore.EnsureNoReparse(directory); AccountStore.EnsureNoReparse(key); AccountStore.EnsureNoReparse(accounts);
                if (AccountStore.Exists(Path.Combine(directory, "accounts.v2.json.tmp"))) throw new MigrationOperationException("MIGRATION_BUSY", "旧版可能正在写入，请关闭旧版后重试。");
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                    hash.AppendData(BitConverter.GetBytes(result.ProtectedKey.Length)); hash.AppendData(result.ProtectedKey); hash.AppendData(result.ciphertext);
                    result.Fingerprint = hash.GetHashAndReset();
                }
                if (expectedFingerprint is not null && !CryptographicOperations.FixedTimeEquals(expectedFingerprint, result.Fingerprint))
                    throw new MigrationOperationException("MIGRATION_CHANGED", "旧账户已发生变化，请关闭旧版后重新预览。");
                var material = DeviceKey.Unprotect(result.ProtectedKey);
                try {
                    var envelope = JsonSerializer.Deserialize<SecureEnvelope>(result.ciphertext, JsonOptions) ?? throw Invalid();
                    var plaintext = BackupCrypto.Decrypt(envelope, Convert.ToBase64String(material));
                    using (var raw = JsonDocument.Parse(plaintext)) {
                        if (raw.RootElement.ValueKind != JsonValueKind.Array) throw Invalid();
                        foreach (var account in raw.RootElement.EnumerateArray()) {
                            if (account.ValueKind != JsonValueKind.Object) throw Invalid();
                            var fields = account.EnumerateObject().Select(p => p.Name).ToArray();
                            if (fields.Distinct().Count() != fields.Length) throw Invalid();
                            if (fields.Any(name => name is not ("id" or "issuer" or "account" or "secret" or "group" or "color" or "createdAt"))) throw Unsupported();
                        }
                    }
                    result.Accounts = JsonSerializer.Deserialize<List<MfaAccount>>(plaintext, JsonOptions) ?? throw Invalid();
                    if (result.Accounts.Count > 1000) throw Unsupported();
                    for (var i = 0; i < result.Accounts.Count; i++) {
                        var account = result.Accounts[i] ?? throw Invalid();
                        account = account with { Secret = SecretValidation.Normalize(account.Secret), Color = account.Color ?? "blue" };
                        try { AccountStore.ValidateAccount(account); } catch (FormatException) { throw Unsupported(); }
                        result.Accounts[i] = account;
                    }
                    if (result.Accounts.Select(a => a.Id).Distinct().Count() != result.Accounts.Count || result.Accounts.Select(a => a.Secret).Distinct().Count() != result.Accounts.Count) throw Unsupported();
                    try { AccountService.CheckCapacity(result.Accounts); } catch (ArgumentException) { throw Unsupported(); }
                } finally { CryptographicOperations.ZeroMemory(material); }
                return result;
            } catch (MigrationOperationException) { result.Dispose(); throw; }
            catch { result.Dispose(); throw Invalid(); }
        }
        private static byte[] ReadBounded(FileStream file, int maximum) {
            if (file.Length <= 0 || file.Length > maximum) throw Invalid();
            var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
            if (file.ReadByte() != -1) { CryptographicOperations.ZeroMemory(bytes); throw Invalid(); }
            return bytes;
        }
        public void Dispose() { keyFile?.Dispose(); accountFile?.Dispose(); CryptographicOperations.ZeroMemory(ProtectedKey); CryptographicOperations.ZeroMemory(ciphertext); CryptographicOperations.ZeroMemory(Fingerprint); Accounts.Clear(); }
    }
}
