using System.Text.Json;
using LiubaiMfa.Windows.Native;

namespace LiubaiMfa.CoreHost;

internal sealed class QrImportService : IDisposable
{
    private readonly ProtectionSession session;
    private readonly AccountService accounts;
    private readonly Func<string, Task<IReadOnlyList<string>>> decode;
    private readonly TimeSpan lifetime;
    private readonly Timer expiryTimer;
    private PendingQr? pending;
    private long operationEpoch;
    private int decodeInFlight;
    private bool disposed;
    private sealed record PendingQr(string Token, long Generation, long Deadline, QrDraft? Draft);
    private sealed record DecodeTicket(string Token, string Path, long Epoch);

    public QrImportService(AccountStore store, ProtectionSession session, TimeSpan? lifetime = null, Func<string, Task<IReadOnlyList<string>>>? decoder = null)
    {
        this.session = session; accounts = new AccountService(store, _ => false);
        this.lifetime = lifetime ?? TimeSpan.FromSeconds(120); decode = decoder ?? new QrImageDecoder().DecodeAsync;
        expiryTimer = new Timer(_ => { lock (session.Gate) { if (pending is not null && Expired(pending)) Cancel(); } });
        session.Invalidated += Cancel;
    }
    public object Handle(string method, JsonElement payload)
    {
        lock (session.Gate)
        {
            if (disposed) throw ExpiredError();
            if (method == "qrCancel") { Exact(payload); Cancel(); return new { cancelled = true }; }
            RequireUnlocked();
            if (method == "qrBegin")
            {
                Exact(payload); Cancel();
                var token = Guid.NewGuid().ToString("N");
                pending = new(token, session.Generation, Environment.TickCount64 + (long)lifetime.TotalMilliseconds, null);
                expiryTimer.Change(lifetime, Timeout.InfiniteTimeSpan);
                return new { token };
            }
            if (method != "qrCommit") throw new ArgumentException("输入内容无效，请检查各字段。");
            Exact(payload, "token", "issuer", "account", "group", "color");
            var state = GetPending(Text(payload, "token", 64));
            if (state.Draft is null) throw ExpiredError();
            var input = JsonSerializer.SerializeToElement(new { issuer = Text(payload, "issuer", 80), account = Text(payload, "account", 120), group = Text(payload, "group", 40, true), color = Text(payload, "color", 16), secret = state.Draft.Secret });
            var result = accounts.Handle("addAccount", input);
            Cancel(); return result;
        }
    }
    public async Task<object> DecodeAsync(JsonElement payload)
    {
        var ticket = StartDecode(payload);
        try
        {
            IReadOnlyList<string> texts;
            try { texts = await decode(ticket.Path); }
            catch (QrImageException error) { return FinishDecode(ticket, null, new QrOperationException(error.Code, error.Message)); }
            return FinishDecode(ticket, texts, null);
        }
        finally { Interlocked.Exchange(ref decodeInFlight, 0); }
    }
    private DecodeTicket StartDecode(JsonElement payload)
    {
        // Monitor 只存在于同步 helper 内；图片解码和 await 不持有 Gate。
        lock (session.Gate)
        {
            RequireUnlocked(); Exact(payload, "token", "path");
            var state = GetPending(Text(payload, "token", 64));
            var path = Text(payload, "path", 32767);
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("请选择有效的图片路径。");
            if (Interlocked.CompareExchange(ref decodeInFlight, 1, 0) != 0) throw new QrOperationException("QR_BUSY", "上一张图片仍在识别，请稍后重试。");
            pending = state with { Draft = null };
            return new(state.Token, path, ++operationEpoch);
        }
    }
    private object FinishDecode(DecodeTicket ticket, IReadOnlyList<string>? texts, QrOperationException? error)
    {
        lock (session.Gate)
        {
            RequireUnlocked();
            var state = GetPending(ticket.Token);
            if (ticket.Epoch != operationEpoch) throw ExpiredError();
            if (error is not null) throw error;
            if (texts!.Count == 0) throw new QrOperationException("QR_NOT_FOUND", "未识别到二维码，请换一张清晰图片。");
            if (texts.Count != 1) throw new QrOperationException("QR_MULTIPLE", "图片中有多个二维码，请每次只导入一个。");
            var draft = StrictOtpAuthParser.Parse(texts[0]);
            pending = state with { Draft = draft };
            return new { token = state.Token, issuer = draft.Issuer, account = draft.Account, group = "", color = "blue" };
        }
    }
    private PendingQr GetPending(string token)
    {
        if (disposed || pending is null) throw ExpiredError();
        if (Expired(pending) || pending.Generation != session.Generation) { Cancel(); throw ExpiredError(); }
        if (token != pending.Token) throw ExpiredError();
        return pending;
    }
    private static bool Expired(PendingQr value) => Environment.TickCount64 >= value.Deadline;
    private void RequireUnlocked()
    {
        if (session.Pending) throw new QrOperationException("AUTH_PENDING", "身份验证正在进行。");
        if (session.Locked) throw new QrOperationException("LOCKED", "账户已锁定，请先验证身份。");
    }
    public void Cancel() { lock (session.Gate) { if (disposed) return; operationEpoch++; pending = null; expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } }
    public void Dispose() { lock (session.Gate) { if (disposed) return; Cancel(); disposed = true; session.Invalidated -= Cancel; expiryTimer.Dispose(); } }
    private static QrOperationException ExpiredError() => new("QR_EXPIRED", "二维码操作已失效，请重新开始。");
    private static void Exact(JsonElement payload, params string[] required)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("输入内容无效，请检查各字段。");
        var fields = payload.EnumerateObject().ToArray();
        if (fields.Length != required.Length || fields.Select(p => p.Name).Distinct().Count() != fields.Length || required.Any(p => !payload.TryGetProperty(p, out _))) throw new ArgumentException("输入内容无效，请检查各字段。");
    }
    private static string Text(JsonElement payload, string field, int maximum, bool empty = false)
    {
        var value = payload.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("输入内容无效，请检查各字段。");
        var text = value.GetString()!;
        if ((!empty && string.IsNullOrWhiteSpace(text)) || text.Length > maximum || text.Any(char.IsControl)) throw new ArgumentException("输入内容无效，请检查各字段。");
        return text;
    }
}
