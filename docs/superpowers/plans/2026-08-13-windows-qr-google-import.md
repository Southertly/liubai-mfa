# Windows 二维码与 Google Authenticator 导入实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在紧凑 Windows 客户端中加入截图、剪贴板和本地图片二维码识别，并支持标准 TOTP URI 与 Google Authenticator 单张/多张迁移二维码的安全预览和原子导入；暂不支持 HOTP。

**Architecture:** 三种图片入口统一进入 `QrImageDecoder`，解出的文本由 Core 层 `OtpImportParser` 分流到标准 TOTP 或隔离的 Google Protobuf 解析器。Google 多张二维码先进入纯内存 `MigrationBatchSession`，完整后由 `ImportClassifier` 生成预览；用户确认后通过候选快照一次性加密保存，成功才替换界面集合。截图回调使用 MSIX 协议、关联 ID 和 Windows App SDK 单实例重定向，且不能绕过锁定/Hello。

**Tech Stack:** C#、.NET 8、WinUI 3、Windows App SDK 2.3.1、MSIX、ZXing.Net 0.16.11、Google.Protobuf 3.35.1、Grpc.Tools 2.83.0（PrivateAssets=All）。

---

## 前置条件与安全边界

- 先完成 `2026-08-13-windows-compact-ui-redesign.md`；本计划复用其中的 `IAccountRepository`、平台 regression 项目、紧凑顶栏、`InfoBar` 和对话服务边界。
- 工作目录与分支固定为 `E:\project\MFA\mfa-verifier\artifacts\windows`、`codex/windows-mvp`。
- `Package.appxmanifest`、`README.md`、`VERIFICATION.md`、证书脚本目前有用户未提交改动。修改前后都要逐文件 `git diff`，不得覆盖或整文件重写。
- 测试夹具只使用 RFC 向量和代码生成的无真实账户 payload；不得把用户二维码、截图、Base64、Protobuf 字节、密钥、验证码或备份正文写入仓库、日志或聊天。
- 不新增摄像头扫描、HOTP 生成、云同步、网络图标或远程解析 API。

## Task 1：扩展 TOTP 模型并用 RFC 6238 固定向量驱动实现

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Core/Models/MfaAccount.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Models/TotpAlgorithm.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Models/TotpParameters.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core/TotpService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core.Regression/Program.cs`

**Step 1: 先写 RFC 6238 失败测试**

用 RFC 6238 附录 B 的 ASCII 测试密钥与时间点 `59` 测试 8 位结果：

```csharp
AssertEqual("94287082", TotpService.Generate(AsciiToBase32("12345678901234567890"), 59, TotpAlgorithm.Sha1, 8, 30));
AssertEqual("46119246", TotpService.Generate(AsciiToBase32("12345678901234567890123456789012"), 59, TotpAlgorithm.Sha256, 8, 30));
AssertEqual("90693936", TotpService.Generate(AsciiToBase32("1234567890123456789012345678901234567890123456789012345678901234"), 59, TotpAlgorithm.Sha512, 8, 30));
```

再覆盖 6 位截断、自定义周期 `60`、非法 `digits=7`、`period=0`、未知算法和非法 Base32。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: 新枚举/重载不存在或 SHA256/SHA512 向量失败。

**Step 3: 实现参数和值对象**

```csharp
public enum TotpAlgorithm { Sha1, Sha256, Sha512 }

public readonly record struct TotpParameters(TotpAlgorithm Algorithm, int Digits, int Period)
{
    internal static TotpParameters Default => new(TotpAlgorithm.Sha1, 6, 30);
    internal void Validate()
    {
        if (Digits is not (6 or 8)) throw new ArgumentOutOfRangeException(nameof(Digits));
        if (Period is < 5 or > 300) throw new ArgumentOutOfRangeException(nameof(Period));
    }
}
```

`TotpService` 根据算法创建 `HMACSHA1/HMACSHA256/HMACSHA512`，其余动态截断逻辑只保留一份。现有 `Generate(secret)` 和剩余秒数 API 委托给默认参数，保持旧调用兼容。

**Step 4: 扩展 MfaAccount**

新增可选 JSON 字段，并在 `null` 时继续省略：

```csharp
[JsonPropertyName("algorithm")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? Algorithm { get; init; }

[JsonPropertyName("digits")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public int? Digits { get; init; }

[JsonPropertyName("period")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public int? Period { get; init; }

public TotpParameters GetTotpParameters() =>
    TotpParametersParser.FromOptional(Algorithm, Digits, Period);
```

字段缺失严格解释为 SHA1/6/30；默认值保存时允许继续省略。`TotpService` 增加以 `long unixTimeSeconds` 接收固定测试时间的重载，以及直接接收 `TotpParameters` 的正式重载；现有 `DateTimeOffset?` API 保持兼容。非法持久化值必须让加载失败并保持锁定，不能静默降级。

**Step 5: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Core artifacts/windows/LiubaiMfa.Core.Regression/Program.cs
git commit -m "feat: support advanced TOTP parameters"
```

## Task 2：升级标准 otpauth URI 并建立统一分流器

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Core/OtpAuthUriParser.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/OtpImportParser.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/OtpImportResult.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core.Regression/Program.cs`

**Step 1: 写失败测试**

覆盖：默认 SHA1/6/30；显式 SHA256/8/60；label 中 `issuer:account`；query issuer 一致；大小写无关；HOTP、缺 secret、坏 Base32、冲突 issuer、未知算法、digits 7、period 4/301、过长 URI 全部拒绝。

```csharp
var parsed = OtpAuthUriParser.Parse(
    "otpauth://totp/Example:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example&algorithm=SHA256&digits=8&period=60");
AssertEqual(TotpAlgorithm.Sha256, parsed.Parameters.Algorithm);
AssertEqual(8, parsed.Parameters.Digits);
AssertEqual(60, parsed.Parameters.Period);
```

分流器测试断言只接受：

```csharp
OtpImportKind.StandardTotp  // otpauth://totp
OtpImportKind.GoogleMigration // otpauth-migration://offline?data=...
```

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: 参数未解析、HOTP 未明确分类或分流类型不存在。

**Step 3: 实现有界解析**

- URI 文本最大 16 KiB；单 query 值最大 12 KiB。
- scheme、host、path 使用 ordinal-ignore-case 比较；拒绝 user-info、fragment 和非预期 host。
- 先 URL decode 一次，再做长度/枚举/整数检查；禁止重复关键参数。
- Base32 只做合法性验证，不在异常中拼接原始输入。
- `OtpImportParser` 返回判别联合风格结果，不抛出包含原文的异常：

```csharp
internal sealed record OtpImportResult(
    OtpImportKind Kind,
    ParsedTotpAccount? Totp,
    GoogleMigrationEnvelope? Migration,
    ImportError? Error);
```

**Step 4: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Core/OtpAuthUriParser.cs artifacts/windows/LiubaiMfa.Core/Import artifacts/windows/LiubaiMfa.Core.Regression/Program.cs
git commit -m "feat: parse bounded TOTP import URIs"
```

## Task 3：用隔离的 Protobuf 解析 Google Authenticator 迁移二维码

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Core/LiubaiMfa.Core.csproj`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/Protos/OtpMigration.proto`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/GoogleMigrationParser.cs`
- Create: `artifacts/windows/LiubaiMfa.Core.Regression/Fixtures/GoogleMigrationFixtures.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core.Regression/Program.cs`

**Step 1: 固定依赖和最小 schema**

`LiubaiMfa.Core.csproj` 增加：

```xml
<PackageReference Include="Google.Protobuf" Version="3.35.1" />
<PackageReference Include="Grpc.Tools" Version="2.83.0" PrivateAssets="All" />
<Protobuf Include="Import\Protos\OtpMigration.proto" GrpcServices="None" />
```

proto 只声明实际读取字段，并保留 Google 已知 field numbers：

```proto
syntax = "proto3";
option csharp_namespace = "LiubaiMfa.Core.Import.Protos";

message MigrationPayload {
  repeated OtpParameters otp_parameters = 1;
  int32 version = 2;
  int32 batch_size = 3;
  int32 batch_index = 4;
  int32 batch_id = 5;
}

message OtpParameters {
  bytes secret = 1;
  string name = 2;
  string issuer = 3;
  Algorithm algorithm = 4;
  DigitCount digits = 5;
  OtpType type = 6;
  int64 counter = 7;
}

enum Algorithm { ALGORITHM_UNSPECIFIED = 0; ALGORITHM_SHA1 = 1; ALGORITHM_SHA256 = 2; ALGORITHM_SHA512 = 3; ALGORITHM_MD5 = 4; }
enum DigitCount { DIGIT_COUNT_UNSPECIFIED = 0; DIGIT_COUNT_SIX = 1; DIGIT_COUNT_EIGHT = 2; }
enum OtpType { OTP_TYPE_UNSPECIFIED = 0; OTP_TYPE_HOTP = 1; OTP_TYPE_TOTP = 2; }
```

**Step 2: 先生成无真实账户的失败夹具**

夹具代码用 protobuf 对象构造 payload，再编码成 URL-safe Base64；不把真实 Google 导出字符串硬编码进仓库。测试覆盖单账户、多账户、TOTP 三种算法、6/8 位、HOTP、MD5、unknown enum、空 secret/name、坏 Base64、截断 protobuf、过大 payload。

```csharp
var result = GoogleMigrationParser.Parse(GoogleMigrationFixtures.SingleTotp());
AssertEqual(1, result.Accounts.Count);
AssertEqual(TotpAlgorithm.Sha1, result.Accounts[0].Parameters.Algorithm);
AssertEqual(30, result.Accounts[0].Parameters.Period);
```

**Step 3: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: `GoogleMigrationParser` 不存在。

**Step 4: 实现失败关闭解析器**

- `data` URL decode 后最大 64 KiB，Base64url 规范化 padding 后解码。
- 最多 10 张批次、单张最多 50 个账户、总计最多 200 个账户。
- secret 1–128 bytes；name/issuer 各最多 256 UTF-16 字符。
- 只把 `TOTP + SHA1/SHA256/SHA512 + 6/8` 标为可导入，period 固定 30。
- HOTP 返回 `UnsupportedHotp`，MD5/unknown 返回明确不支持，损坏 payload 返回 `InvalidPayload`；绝不猜默认 enum。
- 异常和诊断只携带错误码，不携带 Base64、protobuf 或 secret。

**Step 5: 回归并提交**

```powershell
dotnet restore .\LiubaiMfa.Core\LiubaiMfa.Core.csproj
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Core/LiubaiMfa.Core.csproj artifacts/windows/LiubaiMfa.Core/Import artifacts/windows/LiubaiMfa.Core.Regression
git commit -m "feat: parse Google Authenticator migrations"
```

## Task 4：实现多张迁移会话、分类与去重

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Core/Import/MigrationBatchSession.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/ImportClassifier.cs`
- Create: `artifacts/windows/LiubaiMfa.Core/Import/ImportPreview.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core.Regression/Program.cs`

**Step 1: 写完整会话矩阵测试**

测试单张、多张顺序、乱序、相同张重复、相同 index 内容冲突、不同 batch ID、不同 batch size、index 越界、缺页、取消、锁定清除和 5 分钟超时。时钟通过 `TimeProvider` 注入，不使用 `Thread.Sleep`。

```csharp
var session = new MigrationBatchSession(fakeTimeProvider);
AssertEqual(BatchAddStatus.Accepted, session.Add(Fixtures.BatchPart(index: 1, size: 2, id: 7)).Status);
AssertEqual(BatchAddStatus.Complete, session.Add(Fixtures.BatchPart(index: 0, size: 2, id: 7)).Status);
AssertEqual(new[] { 0, 1 }, session.GetCompletedParts().Select(x => x.BatchIndex));
```

分类测试覆盖四类计数：新增、重复、暂不支持、无效。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: 会话和分类类型不存在。

**Step 3: 实现批次状态机**

`MigrationBatchSession` 第一张固定 `BatchId/BatchSize`，用按 index 的字典保存经过解析的不可变 part 和其 SHA-256 内容指纹；同 index 同指纹返回 `AlreadyRead`，同 index 异指纹返回 `Conflict` 并清除会话。`Cancel`、`OnLocked`、超时都清空账户和元数据。

**Step 4: 实现账户身份和预览分类**

去重键固定为：

```csharp
internal readonly record struct TotpIdentity(
    string NormalizedSecret,
    TotpAlgorithm Algorithm,
    int Digits,
    int Period);
```

Base32 规范化为大写并去除允许的空格/连字符后比较；现有账户和当前批次都要去重。`ImportPreview` 每项只暴露 issuer、account、算法、位数、周期、状态和不含秘密的原因；不暴露 secret 给 UI binding。

**Step 5: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Core/Import artifacts/windows/LiubaiMfa.Core.Regression/Program.cs
git commit -m "feat: classify Google migration batches"
```

## Task 5：把批量导入改成候选快照原子保存

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/MainViewModel.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/ImportPreviewViewModel.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 写保存失败不变性测试**

```csharp
static async Task BatchImportRollsBackWhenSaveFails()
{
    var repository = new FakeAccountRepository(Fixtures.Accounts()) { ThrowOnSave = true };
    var vm = new MainViewModel(repository);
    await vm.LoadAsync();
    var before = vm.AccountsSnapshot.Select(x => x.Id).ToArray();

    var result = await vm.ImportBatchAsync(Fixtures.ValidImportCandidates());

    AssertFalse(result.Succeeded);
    AssertSequence(before, vm.AccountsSnapshot.Select(x => x.Id));
    AssertSequence(before, repository.Stored.Select(x => x.Id));
}
```

再测成功只调用一次 Save、重复项不写入、全部不可导入时不调用 Save。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: `ImportBatchAsync` 不存在或旧实现先改 Observable 导致断言失败。

**Step 3: 实现候选快照提交**

```csharp
var candidateSnapshot = _accounts
    .Concat(importable.Select(CreateAccount))
    .ToArray();

await _repository.SaveAsync(candidateSnapshot, cancellationToken);

_accounts.Clear();
_accounts.AddRange(candidateSnapshot);
RefreshDerivedState();
```

保存调用前不修改 `_accounts`、`VisibleAccounts`、筛选或预览；失败返回“账户未导入，本地数据保持不变”。成功后才清除预览并定位第一个新增账户。

**Step 4: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Windows/ViewModels artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "fix: make Windows batch imports atomic"
```

## Task 6：统一截图、剪贴板和文件的图像解码管线

**Files:**

- Replace: `artifacts/windows/LiubaiMfa.Windows/Services/QrCodeImportService.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/QrImageDecoder.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ClipboardImageService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/NativeFileService.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Fixtures/QrFixtureFactory.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 写解码边界测试**

代码生成不含真实密钥的 PNG/JPEG/BMP QR 图片，测试旋转后的标准 TOTP 文本、无 QR、损坏图片、25 MiB 以上、宽/高超过 10,000、总像素超过 40,000,000。所有来源都调用同一个 decoder API。

```csharp
var text = await decoder.DecodeAsync(QrFixtureFactory.CreatePng("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP"));
AssertTrue(text.StartsWith("otpauth://totp/", StringComparison.Ordinal));
```

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 新 decoder/source API 不存在。

**Step 3: 实现统一 decoder**

`QrImageDecoder.DecodeAsync(IRandomAccessStreamWithContentType, CancellationToken)` 负责字节数、尺寸和像素限制，统一解码为 BGRA8，再调用 ZXing QR 模式、自动旋转和 TryHarder。返回值只含内存中的二维码文本；错误对象只含错误码和中文安全文案。

`ClipboardImageService` 仅在用户点击后调用一次 `Clipboard.GetContent()`，只接受 bitmap，不读取文本/HTML/文件列表，不清空或改写剪贴板。

`NativeFileService` picker 只允许 `.png/.jpg/.jpeg/.bmp`，取消返回 `null`。原 `QrCodeImportService` 改为薄协调器或删除其重复解码逻辑。

**Step 4: 编译、回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.Platform\LiubaiMfa.Windows.Platform.csproj -c Release
git add artifacts/windows/LiubaiMfa.Windows/Services artifacts/windows/LiubaiMfa.Windows.Platform.Regression
git commit -m "feat: unify Windows QR image decoding"
```

## Task 7：接入新版 Snipping Tool 协议和单实例激活

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/Program.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ActivationRouter.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolCaptureService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj`
- Modify carefully: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 先测试回调解析和关联校验**

把 URI 解析做成纯函数，覆盖 200 成功、499 取消、408 超时、错误 correlation ID、重复回调、缺 token、未知 query 和超长 token。测试不得真正启动 Snipping Tool。

```csharp
var result = CaptureCallbackParser.Parse(
    new Uri("liubaimfa://capture-response?code=200&x-request-correlation-id=expected&file-access-token=token"),
    expectedCorrelationId: "expected");
AssertEqual(CaptureCallbackStatus.Success, result.Status);
```

**Step 2: 运行并确认失败，然后实现纯解析器**

只接受 scheme `liubaimfa`、host `capture-response`、当前活动 correlation ID、`code=200/499/408/400/500`。成功只返回短期 token；原因文案不信任外部 `reason`，由本地 code 映射生成。

**Step 3: 注册 MSIX 回调协议**

在 manifest 的现有 `<Extensions>` 中逐段合并：

```xml
<uap:Extension Category="windows.protocol">
  <uap:Protocol Name="liubaimfa" DesiredView="default">
    <uap:DisplayName>留白 MFA 截图回调</uap:DisplayName>
  </uap:Protocol>
</uap:Extension>
```

不得覆盖现有 `runFullTrust`、identity、capabilities 或用户未提交修改。

**Step 4: 实现发起和兑换**

`SnippingToolCaptureService` 生成 GUID 关联 ID，保存单个活动请求及 5 分钟过期时间，发起：

```text
ms-screenclip://capture/image?rectangle&enabledModes=SnippingAllModes&api-version=1.2&user-agent=LiubaiMfa&redirect-uri=liubaimfa://capture-response&x-request-correlation-id={GUID}
```

必须使用 `Launcher.LaunchUriAsync`，不加 `auto-save`。成功回调用 `SharedStorageAccessManager.RedeemTokenForFileAsync` 获取临时 `StorageFile`，打开只读流后立即交给 `QrImageDecoder`，不复制到应用数据目录。

**Step 5: 实现 WinUI 单实例入口**

`LiubaiMfa.Windows.csproj` 加：

```xml
<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
```

`Program.Main` 在创建 XAML 前调用 `AppInstance.FindOrRegisterForKey("LiubaiMfa.Main")`；非当前实例把 `GetActivatedEventArgs()` 通过 `RedirectActivationToAsync` 转交后退出。主实例订阅 `Activated`，将协议激活投递到 UI Dispatcher。普通重复启动只激活现有窗口，不创建第二个窗口。

**Step 6: 与锁定状态串联**

协议回调到达时：先记录不含 token 的待处理状态；若失活已超过 1 分钟，先显示锁定页。只有 Windows Hello 成功且 correlation ID 仍有效时才兑换文件并解码。锁定、取消、过期或进程结束都清除 pending capture 和 Google batch session。

**Step 7: 编译并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
git add artifacts/windows/LiubaiMfa.Windows/Program.cs artifacts/windows/LiubaiMfa.Windows/Services/ActivationRouter.cs artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolCaptureService.cs artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj artifacts/windows/LiubaiMfa.Windows/App.xaml.cs artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git add -p artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest
git commit -m "feat: receive Snipping Tool QR captures"
```

## Task 8：实现导入菜单、Google 批次收集和安全预览

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ImportDialogService.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/ImportPreviewDialog.xaml`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/ImportPreviewDialog.xaml.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/MigrationBatchDialog.xaml`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/MigrationBatchDialog.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`

**Step 1: 创建统一菜单**

“导入二维码”打开 `MenuFlyout`，顺序固定：

1. 截图识别二维码
2. 从剪贴板识别
3. 从图片导入

每项有中文 `AutomationProperties.Name` 和键盘焦点。三个 handler 只负责取得图像流，随后统一调用 `DecodeAndRouteAsync`。

**Step 2: 实现标准 TOTP 确认**

标准 TOTP 解码成功后打开账户确认对话：允许修正 issuer/account/group，不允许修改二维码携带的 algorithm/digits/period；高级参数以只读文字显示。确认后调用原子单项导入路径。

**Step 3: 实现 Google 批次收集**

批次不完整时显示“已读取 X/Y 张”和“继续截图 / 从剪贴板读取 / 选择下一张图片 / 取消导入”。重复张提示“这张二维码已读取”；混批提示“这张二维码不属于当前 Google 导入批次”。关闭、锁定、取消和超时都调用 `session.Clear()`。

**Step 4: 实现预览**

预览顶部显示四个计数；每项只显示服务、账户、TOTP、算法、位数、周期、状态和安全原因。用户可给可导入项统一选组，并补全缺失 issuer；重复、HOTP、MD5、unknown、invalid 项不可勾选。没有新增项时禁用“导入可用账户”。

**Step 5: 保存和反馈**

点击“导入可用账户”仅调用一次 `MainViewModel.ImportBatchAsync`。成功显示导入数量并定位首个新增账户；失败显示“账户未导入，本地数据保持不变”。对话关闭后释放图像、二维码文本、解析结果和 session 引用。

**Step 6: 编译并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
git add artifacts/windows/LiubaiMfa.Windows/Services/ImportDialogService.cs artifacts/windows/LiubaiMfa.Windows/Views artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs
git commit -m "feat: add QR migration import preview"
```

## Task 9：备份兼容性提示与旧数据回归

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Core/BackupCrypto.cs`
- Modify: `artifacts/windows/LiubaiMfa.Core.Regression/Program.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs`

**Step 1: 写旧备份与高级参数测试**

- 现有无 `algorithm/digits/period` 的 v2 fixture 读取后等价 SHA1/6/30。
- 高级 TOTP 导出再导入后参数不丢失。
- v2 外层版本、KDF、nonce、认证和错误行为不变。
- 任一非法高级参数导致备份验证失败，不产生部分账户。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: 高级参数未往返或未校验。

**Step 3: 实现兼容验证与导出警告**

`BackupCrypto` 解密 JSON 后对每个账户调用统一 TOTP 参数验证；不改变加密 envelope。`BackupDialogService` 检测存在非默认 TOTP 参数时，在导出确认对话显示：

“此备份包含 SHA-256、SHA-512、8 位或自定义周期账户，旧版 Android/HarmonyOS 客户端可能无法正确使用。Windows 版可正常恢复。”

不修改 Android/HarmonyOS 代码，不阻止用户继续导出。

**Step 4: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Core/BackupCrypto.cs artifacts/windows/LiubaiMfa.Core.Regression/Program.cs artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs
git commit -m "feat: preserve advanced TOTP backups"
```

## Task 10：端到端验证、真实导入演练和文档合并

**Files:**

- Modify carefully: `artifacts/windows/README.md`
- Modify carefully: `artifacts/windows/VERIFICATION.md`

**Step 1: 全量自动验证**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.Platform\LiubaiMfa.Windows.Platform.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet restore .\LiubaiMfa.sln
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

Expected: 全部退出码 0，0 warning、0 error。

**Step 2: 安装并启动真实 MSIX**

使用仓库现有打包流程；先审计 manifest 和证书脚本未提交差异，逐段合并。安装后启动“留白 MFA”，确认只有一个可交互主窗口。第二次启动应激活原窗口，不创建第二个窗口。

**Step 3: 使用虚构数据完成导入矩阵**

- 截图：标准 TOTP QR 成功；Esc 取消无错误；回调超过 1 分钟先 Hello。
- 剪贴板：图片成功；纯文本、HTML 和文件列表提示无可识别图片且不改剪贴板。
- 文件：PNG/JPEG/JPG/BMP 成功；损坏图和超限图安全失败。
- Google：单张成功；多张乱序成功；重复张不重复计数；混批拒绝；取消/超时/锁定清空。
- 预览计数准确；HOTP 显示暂不支持；MD5/unknown/invalid 不可导入。
- 模拟仓库保存失败时，本地文件和 UI 账户都不增加。
- SHA1/SHA256/SHA512、6/8 位和自定义周期显示的代码与独立参考实现一致。

**Step 4: 安全检查**

检查应用数据目录、Temp、事件日志和控制台输出：没有新增截图副本、二维码原文、Base64、protobuf、secret、OTP 或备份正文。断网运行所有导入流程，确认无网络依赖。

**Step 5: 合并文档**

`README.md` 增加三个导入入口、Google 多张批次、HOTP 不支持和高级 TOTP 兼容性说明。`VERIFICATION.md` 追加准确命令、测试数量、真实 MSIX 版本/时间和逐项结果；保留现有用户编辑。

**Step 6: 最终差异审计并提交**

```powershell
git diff --check
git status --short
git diff --stat
git diff -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest artifacts/windows/README.md artifacts/windows/VERIFICATION.md artifacts/windows/scripts/new-development-certificate.ps1
git add -p artifacts/windows/README.md artifacts/windows/VERIFICATION.md
git commit -m "docs: verify Windows QR migration imports"
```

## 完成门槛

以下全部满足才算完成：三种图片来源共用同一受限 decoder；标准 TOTP 与 Google 单/多张迁移正确分流；HOTP 明确不支持；高级 TOTP 固定向量全绿；批量导入原子提交；截图回调不能绕过锁定；无敏感落盘/日志/联网；真实 MSIX 中完成端到端演练；原有用户未提交改动和其他平台代码保持不变。
