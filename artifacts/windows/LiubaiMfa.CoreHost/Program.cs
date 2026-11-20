using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using LiubaiMfa.CoreHost;
using LiubaiMfa.Windows.Native;

using var store = args.Length == 2 && args[0] == "--data-directory" && Path.IsPathFullyQualified(args[1]) ? new AccountStore(args[1]) : null;
var clipboard = new Lazy<CodeClipboard>(() => new CodeClipboard());
var accountService = store is null ? null : new AccountService(store, code => clipboard.Value.Copy(code));
ProtectionSession? session = null;
BackupService? backupService = null;
QrImportService? qrService = null;
bool ended=false;
const int maximumFrameBytes = 1024 * 1024;
using var input = Console.OpenStandardInput();
using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
using var outputGate = new SemaphoreSlim(1);
using var frame = new MemoryStream();
var buffer = new byte[8192];
var invalidFrames = 0;
var tasks = new HashSet<Task>();
var pendingIds = new PendingRequests();
var wireOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
try {
 while (true) {
    var count = await input.ReadAsync(buffer);
    if (count == 0) break;
    for (var index = 0; index < count; index++) {
        if (buffer[index] != (byte)'\n') {
            if (frame.Length >= maximumFrameBytes) return 2;
            frame.WriteByte(buffer[index]); continue;
        }
        var bytes=frame.ToArray(); frame.SetLength(0);
        var task=HandleAsync(bytes); tasks.Add(task);
        tasks.RemoveWhere(t=>t.IsCompleted);
        if (invalidFrames >= 3 || tasks.Count > 16) return 2;
    }
 }
 return 0;
} finally {
 ended=true; session?.Lock();
 try { await Task.WhenAll(tasks); } catch { }
 backupService?.Dispose();
 qrService?.Dispose();
 if(clipboard.IsValueCreated)clipboard.Value.Dispose();
}

object Status() => new { service="LiubaiMfa.CoreHost",protocolVersion=1,accountAccess=accountService is not null,state="ready",locked=session?.Locked ?? true,protectionEnabled=session?.Enabled ?? true,authenticationPending=session?.Pending ?? false,generation=session?.Generation ?? 0 };
async Task HandleAsync(byte[] bytes)
{
 string? id=null;
 bool ownsId=false;
 object Error(string code,string message="本地核心无法处理此请求。") => new {version=1,id,ok=false,error=new {code,message}};
 object Success(object result) => new {version=1,id,ok=true,result};
 object response;
 try {
    using var document=JsonDocument.Parse(bytes);
    var request=document.RootElement;
    if(request.ValueKind!=JsonValueKind.Object)throw new FormatException();
    if(request.TryGetProperty("id",out var idValue)&&idValue.ValueKind==JsonValueKind.String)id=idValue.GetString();
    var fields=request.EnumerateObject().ToArray();
    if(fields.Length!=4 || fields.Select(p=>p.Name).Distinct().Count()!=4 ||
      !request.TryGetProperty("version",out var version)||!version.TryGetInt32(out var number)||number!=1 ||
      string.IsNullOrWhiteSpace(id)||id.Length>64 || !request.TryGetProperty("method",out var method)||method.ValueKind!=JsonValueKind.String ||
      !request.TryGetProperty("payload",out var payload)||payload.ValueKind!=JsonValueKind.Object)throw new FormatException();
    if(!pendingIds.TryEnter(id))throw new FormatException();
    ownsId=true; invalidFrames=0;
    var operation=method.GetString()!;
    if(operation=="handshake") {
        if(session is not null || store is null || payload.EnumerateObject().Count()!=1 || !payload.TryGetProperty("hwnd",out var handle) || handle.ValueKind!=JsonValueKind.String || !long.TryParse(handle.GetString(),out var value) || !TrustedWindow.IsParentWindow((nint)value))response=Error("INVALID_WINDOW");
        else { session=new ProtectionSession(store,new WindowsAuthentication((nint)value)); backupService=new BackupService(store,session); qrService=new QrImportService(store,session); response=Success(Status()); }
    } else if(operation is "unlock" or "setProtection") {
        bool? enabled=null;
        if(operation=="setProtection") {
            if(payload.EnumerateObject().Count()!=1 || !payload.TryGetProperty("enabled",out var setting) || setting.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException();
            enabled=setting.GetBoolean();
        } else if(payload.EnumerateObject().Any())throw new ArgumentException();
        if(session is null)response=Error("HANDSHAKE_REQUIRED");
        else if(session.Pending)response=Error("AUTH_PENDING","身份验证正在进行。");
        else response=await session.VerifyAsync(enabled)?Success(Status()):Error("AUTH_CANCELLED","身份验证未完成，请重试。");
    } else if(operation=="lock") {
        if(payload.EnumerateObject().Any())throw new ArgumentException();
        session?.Lock(); response=Success(Status());
    } else if(operation is "exportPrepare" or "exportSave" or "importPreview" or "importCommit" or "backupCancel") {
        if(backupService is null)response=Error("HANDSHAKE_REQUIRED");
        else response=Success(operation=="exportPrepare" ? await backupService.PrepareExportAsync(payload) : backupService.Handle(operation,payload));
    } else if(operation is "qrBegin" or "qrDecode" or "qrCommit" or "qrCancel") {
        if(qrService is null)response=Error("HANDSHAKE_REQUIRED");
        else response=Success(operation=="qrDecode" ? await qrService.DecodeAsync(payload) : qrService.Handle(operation,payload));
    } else if(operation is "accounts" or "addAccount" or "editAccount" or "createGroup" or "deleteGroup" or "renameGroup" or "moveAccount" or "deleteAccount" or "copyCode") {
        if(session is null || accountService is null)response=Error("HANDSHAKE_REQUIRED");
        else response=Success(accountService.HandleProtected(session,operation,payload));
    } else {
        if(payload.EnumerateObject().Any())throw new ArgumentException();
        response=operation switch {
            "status"=>Success(Status()),
            "ping"=>Success(new {utc=DateTimeOffset.UtcNow}),
            _=>Error("UNKNOWN_METHOD")
        };
    }
 } catch(Exception exception) when(exception is JsonException or InvalidOperationException or FormatException) {
    invalidFrames++; response=Error("INVALID_REQUEST");
 } catch(WindowsAuthenticationException) {response=Error("AUTH_UNAVAILABLE","系统身份验证暂不可用，请稍后重试。");}
 catch(AccountInputException error) {response=Error(error.Code,error.Message);}
 catch(BackupOperationException error) {response=Error(error.Code,error.Message);}
 catch(QrOperationException error) {response=Error(error.Code,error.Message);}
 catch(ArgumentException error) {response=Error("INVALID_REQUEST",error.Message.StartsWith("Value")?"输入内容无效，请检查各字段。":error.Message);}
 catch { session?.Lock(); response=Error("STORE_UNAVAILABLE","无法读取或写入本地账户，已阻止更改。请检查文件或关闭其他实例后重新连接。"); }
 if(ended){if(ownsId)pendingIds.Exit(id!);return;}
 await outputGate.WaitAsync();
 try {if(!ended)await output.WriteLineAsync(JsonSerializer.Serialize(response,wireOptions));}
 finally {if(ownsId)pendingIds.Exit(id!);outputGate.Release();}
}
