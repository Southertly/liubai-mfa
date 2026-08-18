# Windows 剪贴板失败保护 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 当 Windows 剪贴板暂时不可用时，复制验证码显示克制的失败提示并保持应用运行。

**Architecture:** 新增一个职责单一的 `ClipboardService`，由它封装 WinRT 剪贴板写入及 `COMException` 边界，并只向界面返回布尔结果。`MainWindow` 继续负责解锁会话校验和状态提示，不再直接调用剪贴板 API；平台回归通过注入写入委托稳定模拟成功与 `0x800401D0` 失败。

**Tech Stack:** C#、.NET 8、WinUI 3、Windows App SDK 2.3.1、现有控制台回归项目、MSIX。

---

## 文件结构

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ClipboardService.cs` — 封装 WinRT 剪贴板写入和互操作异常。
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs` — 增加剪贴板服务与主窗口接线回归。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs` — 根据复制结果显示成功或警告提示。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/AccountDialogService.cs` — MFA 密钥复制失败时显示警告，不显示成功。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs` — 恢复密钥复制失败时保持备份保存按钮禁用。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest` — 保留 `runFullTrust` 并将测试包版本升级到 `1.0.0.2`，允许覆盖已安装的 `1.0.0.1`。
- Modify: `artifacts/windows/VERIFICATION.md` — 记录自动化和真机回归证据。

### Task 1: 建立可测试的剪贴板错误边界

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ClipboardService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 写失败回归测试**

在 `checks` 列表末尾加入：

```csharp
("剪贴板写入成功且只调用一次", () => RunSync(VerifyClipboardWriteSuccess)),
("剪贴板 COM 失败转换为中性结果", () => RunSync(VerifyClipboardWriteComFailure)),
("剪贴板拒绝空白验证码", () => RunSync(VerifyClipboardRejectsBlankText)),
```

在 `Program.cs` 的辅助方法区域加入：

```csharp
static void VerifyClipboardWriteSuccess()
{
    var writeCount = 0;
    string? writtenText = null;
    var service = new ClipboardService(text =>
    {
        writeCount++;
        writtenText = text;
    });

    Assert(service.TryCopyText("123456"), "正常剪贴板写入应返回成功。");
    Assert(writeCount == 1, "一次复制只能调用一次剪贴板写入。");
    Assert(writtenText == "123456", "剪贴板写入内容错误。");
}

static void VerifyClipboardWriteComFailure()
{
    var writeCount = 0;
    var service = new ClipboardService(_ =>
    {
        writeCount++;
        throw new System.Runtime.InteropServices.COMException(
            "模拟剪贴板占用",
            unchecked((int)0x800401D0));
    });

    Assert(!service.TryCopyText("123456"), "剪贴板 COM 失败应返回 false，不能逸出异常。");
    Assert(writeCount == 1, "剪贴板失败后不得后台重试。");
}

static void VerifyClipboardRejectsBlankText()
{
    var service = new ClipboardService(_ => throw new InvalidOperationException("空白文本不应进入写入委托。"));

    AssertThrows<ArgumentException>(
        () => service.TryCopyText("   "),
        "空白验证码必须在进入系统剪贴板前被拒绝。");
}
```

- [ ] **Step 2: 运行回归并确认 RED**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 非零退出，编译器报告 `ClipboardService` 不存在；不得因为测试拼写或项目路径错误失败。

- [ ] **Step 3: 写最小生产实现**

创建 `ClipboardService.cs`：

```csharp
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace LiubaiMfa.Windows.Services;

public sealed class ClipboardService
{
    private readonly Action<string> _writeText;

    public ClipboardService()
        : this(WriteText)
    {
    }

    internal ClipboardService(Action<string> writeText)
    {
        ArgumentNullException.ThrowIfNull(writeText);
        _writeText = writeText;
    }

    public bool TryCopyText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            _writeText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static void WriteText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
```

- [ ] **Step 4: 运行回归并确认 GREEN**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: `69 项 Windows 平台回归检查通过。`

- [ ] **Step 5: 提交服务和测试**

```powershell
git add -- .\LiubaiMfa.Windows\Services\ClipboardService.cs .\LiubaiMfa.Windows.Platform.Regression\Program.cs
git -c user.name='Codex' -c user.email='codex@openai.com' commit -m 'fix: contain clipboard write failures'
```

### Task 2: 主窗口按复制结果显示正确提示

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: 写主窗口接线失败测试**

在 `checks` 列表末尾加入：

```csharp
("主窗口复制流程使用安全结果边界", () => RunSync(VerifyClipboardResultBoundaryInMainWindow)),
```

在 `FindWorkspaceSourceFile` 附近加入：

```csharp
static void VerifyClipboardResultBoundaryInMainWindow()
{
    var sourcePath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "MainWindow.xaml.cs"));
    var source = File.ReadAllText(sourcePath);

    Assert(source.Contains("if (!_clipboard.TryCopyText(account.Code))", StringComparison.Ordinal),
        "主窗口必须根据剪贴板服务结果决定提示。");
    Assert(source.Contains("剪贴板暂时不可用，请稍后重试。", StringComparison.Ordinal),
        "复制失败提示必须克制且可执行。");
    Assert(!source.Contains("Clipboard.SetContent", StringComparison.Ordinal),
        "主窗口不得继续直接调用系统剪贴板。");
    Assert(!source.Contains("private static void CopyText", StringComparison.Ordinal),
        "主窗口不得保留无错误边界的旧复制方法。");
}
```

- [ ] **Step 2: 运行回归并确认 RED**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 前 69 项通过，随后在“主窗口复制流程使用安全结果边界”失败，提示缺少 `_clipboard.TryCopyText`。

- [ ] **Step 3: 将主窗口接到剪贴板服务**

从 `MainWindow.xaml.cs` 删除：

```csharp
using Windows.ApplicationModel.DataTransfer;
```

在字段区加入：

```csharp
private readonly ClipboardService _clipboard;
```

在构造函数服务初始化区加入：

```csharp
_clipboard = new ClipboardService();
```

将 `CopyCode_Click` 的末尾替换为：

```csharp
if (!_clipboard.TryCopyText(account.Code))
{
    ShowStatus(token, "剪贴板暂时不可用，请稍后重试。", InfoBarSeverity.Warning);
    return;
}

ShowStatus(token, "验证码已复制。", InfoBarSeverity.Success);
```

删除旧方法：

```csharp
private static void CopyText(string text)
{
    var package = new DataPackage();
    package.SetText(text);
    Clipboard.SetContent(package);
    Clipboard.Flush();
}
```

- [ ] **Step 4: 运行平台回归和真实代码编译**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
```

Expected: Platform `70/70`；CodeBehindCheck 与真实 WinUI XAML 编译均为 0 警告、0 错误。

- [ ] **Step 5: 提交主窗口接线**

```powershell
git add -- .\LiubaiMfa.Windows\MainWindow.xaml.cs .\LiubaiMfa.Windows.Platform.Regression\Program.cs
git -c user.name='Codex' -c user.email='codex@openai.com' commit -m 'fix: report unavailable clipboard safely'
```

### Task 2A: 敏感密钥复制必须消费失败结果

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/AccountDialogService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs`

- [ ] **Step 1: 写失败回归测试**

在 `checks` 列表末尾加入：

```csharp
("敏感密钥复制必须消费剪贴板失败结果", () => RunSync(VerifySensitiveClipboardResultHandling)),
```

在源文件契约测试附近加入：

```csharp
static void VerifySensitiveClipboardResultHandling()
{
    var accountDialogPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Services", "AccountDialogService.cs"));
    var accountDialog = File.ReadAllText(accountDialogPath);
    Assert(accountDialog.Contains("Func<string, bool> copyText", StringComparison.Ordinal),
        "MFA 密钥对话框必须接收可判断成功的复制回调。");
    Assert(accountDialog.Contains("if (!copyText(current.Secret))", StringComparison.Ordinal),
        "MFA 密钥复制失败必须被显式处理。");
    Assert(accountDialog.Contains("剪贴板暂时不可用，请稍后重试。", StringComparison.Ordinal),
        "MFA 密钥复制失败必须显示克制警告。");

    var backupDialogPath = FindWorkspaceSourceFile(Path.Combine("LiubaiMfa.Windows", "Services", "BackupDialogService.cs"));
    var backupDialog = File.ReadAllText(backupDialogPath);
    Assert(backupDialog.Contains("Func<string, bool> copyText", StringComparison.Ordinal),
        "恢复密钥对话框必须接收可判断成功的复制回调。");
    Assert(backupDialog.Contains("if (!copyText(recoveryKey))", StringComparison.Ordinal),
        "恢复密钥复制失败必须被显式处理。");
    Assert(backupDialog.Contains("复制失败，请重试", StringComparison.Ordinal),
        "恢复密钥复制失败必须提示重试。");
    Assert(backupDialog.Contains("dialog.IsPrimaryButtonEnabled = false;", StringComparison.Ordinal),
        "恢复密钥复制失败后必须保持保存按钮禁用。");
}
```

- [ ] **Step 2: 运行回归并确认 RED**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 前 70 项通过，第 71 项因现有 `Action<string>` 回调没有消费失败结果而失败。

- [ ] **Step 3: 修改 MFA 密钥复制回调**

将 `AccountDialogService.ShowSecretAsync` 的 `Action<string> copyText` 改为：

```csharp
Func<string, bool> copyText
```

将成功提示前的复制逻辑改为：

```csharp
if (!copyText(current.Secret))
{
    _showStatus(token, "剪贴板暂时不可用，请稍后重试。", InfoBarSeverity.Warning);
    return;
}

_showStatus(token, "MFA 密钥已复制。", InfoBarSeverity.Success);
```

- [ ] **Step 4: 修改恢复密钥复制回调**

将 `BackupDialogService.ConfirmExportAsync` 的 `Action<string> copyText` 改为：

```csharp
Func<string, bool> copyText
```

将复制按钮处理器改为：

```csharp
RoutedEventHandler copyHandler = (_, _) =>
{
    if (!_isSessionCurrent(token))
    {
        return;
    }

    if (!copyText(recoveryKey))
    {
        copyButton.Content = "复制失败，请重试";
        dialog.IsPrimaryButtonEnabled = false;
        return;
    }

    copyButton.Content = "恢复密钥已复制";
    dialog.IsPrimaryButtonEnabled = true;
};
```

在 `MainWindow` 的两个调用点直接传递方法组：

```csharp
_clipboard.TryCopyText
```

- [ ] **Step 5: 运行回归和真实编译**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
```

Expected: Platform `71/71`；两个构建均为 0 警告、0 错误。

- [ ] **Step 6: 自审并提交**

```powershell
git diff --check
git add -- .\LiubaiMfa.Windows.Platform.Regression\Program.cs .\LiubaiMfa.Windows\MainWindow.xaml.cs .\LiubaiMfa.Windows\Services\AccountDialogService.cs .\LiubaiMfa.Windows\Services\BackupDialogService.cs
git -c user.name='Codex' -c user.email='codex@openai.com' commit -m 'fix: honor sensitive clipboard failures'
```

### Task 3: 完整构建、MSIX 真机复测和验证记录

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`
- Modify: `artifacts/windows/VERIFICATION.md`

- [ ] **Step 1: 准备可升级的测试包清单**

将 manifest 的 Identity 版本设为 `1.0.0.2`，并确认受限能力保持：

```xml
<Identity Name="com.mfaverifier.app.windows" Publisher="CN=LiubaiMFA-Development" Version="1.0.0.2" />
```

```xml
<Capabilities>
  <rescap:Capability Name="runFullTrust" />
</Capabilities>
```

- [ ] **Step 2: 运行完整自动化验证**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.Platform\LiubaiMfa.Windows.Platform.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet restore .\LiubaiMfa.sln -p:Platform=x64
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64 --no-restore
git diff --check
```

Expected: Core `5/5`、Platform `71/71`，全部 Release x64 构建 0 警告、0 错误，`git diff --check` 无输出。

- [ ] **Step 3: 生成并安装升级 MSIX**

Run:

```powershell
$liubaiCertificate = Get-ChildItem 'Cert:\CurrentUser\My' |
    Where-Object Subject -eq 'CN=LiubaiMFA-Development' |
    Select-Object -First 1
if ($null -eq $liubaiCertificate) { throw '未找到 Liubai MFA 开发证书。' }

.\scripts\build-msix.ps1 -CertificateThumbprint $liubaiCertificate.Thumbprint
Add-AppxPackage -Path '.\AppPackages\LiubaiMfa.Windows_1.0.0.2_x64_Test\LiubaiMfa.Windows_1.0.0.2_x64.msix'
Get-AppxPackage -Name 'com.mfaverifier.app.windows' |
    Select-Object Name, Version, Architecture, Status
```

Expected: MSIX 生成和升级安装成功；已安装版本为 `1.0.0.2`、架构 `X64`、状态 `Ok`。允许缺少 `mspdbcmf.exe` 的符号包警告，但主 MSIX 必须生成。

- [ ] **Step 4: 真实界面复现验收**

先创建测试时间标记，再启动已安装应用：

```powershell
$liubaiQaMarker = Join-Path $env:TEMP 'liubai-mfa-clipboard-qa.marker'
New-Item -ItemType File -Path $liubaiQaMarker -Force | Out-Null
```

由用户本人完成 Windows Hello；使用现有纯测试账户执行以下路径：

1. 切换到“个人”分组。
2. 点击 GitHub 测试账户的复制按钮。
3. 切回“全部”分组。
4. 重复步骤 1–3 三次，并等待成功提示自动关闭。
5. 确认应用保持运行、六个账户仍完整显示、分组和搜索仍可使用。

随后用时间标记检查测试时段内没有新的应用崩溃：

```powershell
$liubaiQaMarker = Join-Path $env:TEMP 'liubai-mfa-clipboard-qa.marker'
$liubaiQaStart = (Get-Item -LiteralPath $liubaiQaMarker -ErrorAction Stop).LastWriteTime
$liubaiCrashes = Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    StartTime = $liubaiQaStart
} -ErrorAction Stop | Where-Object {
    $_.ProviderName -eq 'Application Error' -and
    $_.Message -match 'LiubaiMfa.Windows.exe'
}
if ($liubaiCrashes) { throw '真机复测期间出现新的 Liubai MFA 崩溃。' }
Remove-Item -LiteralPath $liubaiQaMarker -Force
```

Expected: 应用不退出，Windows 应用程序日志中没有测试开始后的新崩溃事件。

- [ ] **Step 5: 更新验证记录**

只有 Step 2–4 全部通过后，向 `VERIFICATION.md` 追加：

```markdown
## 2026-08-18 剪贴板崩溃真机回归

- Windows 错误报告确认旧版本在剪贴板占用时产生 `0x800401D0`，未捕获异常会终止 WinUI 进程。
- 新增剪贴板错误边界：一次写入失败返回中性结果，不重试、不记录验证码或密钥。
- `LiubaiMfa.Core.Regression`：5/5 通过。
- `LiubaiMfa.Windows.Platform.Regression`：71/71 通过。
- Platform、CodeBehindCheck、真实 WinUI XAML 和完整 solution Release x64 构建：0 警告、0 错误。
- MSIX `1.0.0.2` 已完成生成、升级安装和启动验证。
- 使用六个纯测试账户重复执行“个人分组 → 复制验证码 → 全部分组”三次，应用保持运行；测试时段无新增 `LiubaiMfa.Windows.exe` 崩溃事件。
```

- [ ] **Step 6: 审计并提交 Task 3**

Run:

```powershell
rg -n "exception\.Message|ex\.Message|Clipboard\.SetContent|private static void CopyText" .\LiubaiMfa.Windows -S
git diff --check
git status --short
```

Expected: `exception.Message`/`ex.Message` 无命中；`Clipboard.SetContent` 只在 `ClipboardService.cs` 命中；旧 `CopyText` 无命中；工作树只包含本任务预期的 manifest 与验证记录。

Commit:

```powershell
git add -- .\LiubaiMfa.Windows\Package.appxmanifest .\VERIFICATION.md
git -c user.name='Codex' -c user.email='codex@openai.com' commit -m 'test: verify clipboard crash fix on Windows'
```
