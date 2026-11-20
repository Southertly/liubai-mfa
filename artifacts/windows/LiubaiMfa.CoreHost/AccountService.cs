using System.Text.Json;
using LiubaiMfa.Core;
using LiubaiMfa.Core.Models;
using LiubaiMfa.Windows.Native;

namespace LiubaiMfa.CoreHost;

internal sealed class AccountService(AccountStore store, Func<string,bool> copyCode)
{
    public object HandleProtected(ProtectionSession session, string method, JsonElement payload)
    {
        // Gate 完整留在同步调用中，避免协议异步状态机的异常路径遗留 Monitor。
        lock (session.Gate)
        {
            if (session.Locked || session.Pending) throw new AccountInputException("LOCKED", "账户已锁定，请先验证身份。");
            return Handle(method, payload);
        }
    }
    public object Handle(string method, JsonElement payload)
    {
        var required = method switch
        {
            "accounts" => Array.Empty<string>(),
            "addAccount" => ["issuer", "account", "group", "color", "secret"],
            "editAccount" => ["id", "issuer", "account", "group", "color"],
            "renameGroup" => ["group", "name"],
            "createGroup" => ["name"],
            "deleteGroup" => ["group"],
            "moveAccount" => ["id", "group"],
            "deleteAccount" or "copyCode" => ["id"],
            _ => throw new ArgumentException()
        };
        var fields = payload.EnumerateObject().ToArray();
        if (fields.Length != required.Length || fields.Select(f => f.Name).Distinct().Count() != fields.Length || required.Any(f => !payload.TryGetProperty(f, out _))) throw new ArgumentException("输入内容无效，请检查各字段。");
        string Text(string name, int max, bool empty = false, bool trim = true)
        {
            var field = payload.GetProperty(name);
            if (field.ValueKind != JsonValueKind.String) throw new ArgumentException("输入内容无效，请检查各字段。");
            var value = field.GetString()!;
            if (trim) value = value.Trim();
            if ((!empty && value.Length == 0) || value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("输入内容无效，请检查各字段。");
            return value;
        }
        var accounts = store.Load();
        var groups = store.LoadGroups(accounts);
        if (method == "accounts") return List(accounts);
        if (method == "deleteGroup")
        {
            var group = Text("group", 40, trim: false);
            if (!groups.Contains(group, StringComparer.Ordinal)) throw new AccountInputException("GROUP_NOT_FOUND", "此分组已不存在，请刷新后重试。");
            if (accounts.Any(account => account.Group == group)) throw new AccountInputException("GROUP_NOT_EMPTY", "此分组还有账户，请先移出全部账户。");
            groups.Remove(group);store.SaveGroups(groups);return List(accounts);
        }
        if (method == "createGroup")
        {
            var name = Text("name", 40);
            if (groups.Any(group => string.Equals(group, name, StringComparison.OrdinalIgnoreCase))) throw new AccountInputException("GROUP_EXISTS", "已有同名分组，请使用其他名称。");
            if (groups.Count >= 1000) throw new AccountInputException("GROUP_LIMIT", "分组数量已达上限。");
            groups.Add(name); CheckCapacity(accounts, groups); store.SaveGroups(groups); return List(accounts);
        }
        if (method == "renameGroup")
        {
            var group = Text("group", 40, trim: false); var name = Text("name", 40);
            if (!groups.Contains(group, StringComparer.Ordinal)) throw new AccountInputException("GROUP_NOT_FOUND", "此分组已不存在，请刷新后重试。");
            if (groups.Any(existing => existing != group && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) throw new AccountInputException("GROUP_EXISTS", "已有同名分组，请使用其他名称。");
            if (group == name) return List(accounts);
            groups = groups.Select(existing => existing == group ? name : existing).ToList();
            var occupied = accounts.Any(account => account.Group == group);
            accounts = accounts.Select(account => account.Group == group ? account with { Group = name } : account).ToList();
            CheckCapacity(accounts, groups);
            if (occupied) store.SaveState(accounts, groups); else store.SaveGroups(groups);
            return List(accounts);
        }
        var id = method == "addAccount" ? Guid.NewGuid().ToString("N") : Text("id", 64, trim: method != "moveAccount");
        var existing = accounts.Find(a => a.Id == id);
        if (method != "addAccount" && existing is null) throw new ArgumentException("输入内容无效，请检查各字段。");
        if (method == "moveAccount")
        {
            var group = Text("group", 40, true, trim: false);
            if (group.Length > 0 && !groups.Contains(group, StringComparer.Ordinal)) throw new AccountInputException("GROUP_NOT_FOUND", "此分组已不存在，请刷新后重试。");
            if ((existing!.Group ?? "") == group) return List(accounts);
            accounts[accounts.IndexOf(existing)] = existing with { Group = group };
            CheckCapacity(accounts, groups);
            store.SaveState(accounts, groups);
            return List(accounts);
        }
        if (method == "copyCode") return new { copied = copyCode(TotpService.Generate(existing!.Secret)) };
        if (method == "deleteAccount") accounts.Remove(existing!);
        else
        {
            var issuer = Text("issuer", 80); var account = Text("account", 120); var group = Text("group", 40, true); var color = Text("color", 16);
            if (!AccountStore.IsSupportedColor(color)) throw new ArgumentException("输入内容无效，请检查各字段。");
            if (method == "addAccount" && accounts.Count >= 1000) throw new ArgumentException("输入内容无效，请检查各字段。");
            var secret = existing?.Secret ?? SecretValidation.Normalize(Text("secret", 1024));
            if (existing is null && accounts.Any(a => a.Secret == secret)) throw new AccountInputException("DUPLICATE_ACCOUNT","此密钥已存在，请勿重复添加。");
            var updated = new MfaAccount { Id = id, Issuer = issuer, Account = account, Group = group, Color = color, Secret = secret, CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            if (existing is not null) accounts[accounts.IndexOf(existing)] = updated; else accounts.Add(updated);
        }
        groups = groups.Concat(accounts.Select(account => account.Group ?? "").Where(name => name.Length > 0)).Distinct(StringComparer.Ordinal).ToList();
        GroupCatalog.Validate(groups); CheckCapacity(accounts, groups);
        store.SaveState(accounts, groups);
        return List(accounts);
    }
    internal static void CheckCapacity(List<MfaAccount> accounts, List<string>? groups = null)
    {
        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        // 写盘前按实际协议编码计量，拒绝无法完整返回的结果，避免写成无法读取的库。
        if (accounts.Count > 1000 || JsonSerializer.SerializeToUtf8Bytes(Result(accounts, groups ?? accounts.Select(account => account.Group ?? "").Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList()), options).Length > 1024 * 1024 - 512)
            throw new ArgumentException("输入内容无效，请检查各字段。");
    }
    private object List(List<MfaAccount> accounts) => Result(accounts, store.LoadGroups(accounts));
    private static object Result(List<MfaAccount> accounts, List<string> groups)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = (now.ToUnixTimeSeconds() / 30 + 1) * 30000;
        return new { groups, accounts = accounts.Select(a => new { id = a.Id, issuer = a.Issuer, account = a.Account, group = a.Group ?? "", color = a.Color ?? "blue", code = TotpService.Generate(a.Secret, now), expiresAt }).ToArray() };
    }
}
