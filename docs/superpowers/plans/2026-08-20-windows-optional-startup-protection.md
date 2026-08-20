# Windows 可选启动保护 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将“打开应用时使用 Windows Hello”改为默认关闭、用户主动启用的设置，同时保留敏感操作验证和现有锁定清理边界。

**Architecture:** 新增可独立测试的启动保护策略和可注入的本地设置存储，WinUI 对话框只收集用户选择，`MainWindow` 负责验证、持久化和生命周期编排。关闭保护与 Hello 解锁成功最终汇合到同一条账户加载和会话建立路径，避免产生两套安全行为。

**Tech Stack:** C# 12、.NET 8、WinUI 3、Windows App SDK 2.3.1、Windows Hello、Windows.Storage.ApplicationData、现有控制台回归项目

---

## 文件结构与职责

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/StartupProtectionSettings.cs`
  - 只负责读取和写入非敏感的启动保护布尔设置。
- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/StartupProtectionPolicy.cs`
  - 只负责可测试的启动、手动锁定、自动锁定和设置切换判断。
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/ProtectionSettingsDialogService.cs`
  - 只负责显示设置并返回用户请求的新状态，不调用 Hello 或写设置。
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
  - 为缺省值、持久化代理和纯策略增加行为测试。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
  - 增加设置入口、具名锁定按钮和真实保护状态文案。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
  - 暴露设置事件并更新锁定按钮和保护状态。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
  - 编排启动分流、公共加载路径、设置变更验证和自动锁定开关。
- Modify: `artifacts/windows/README.md`、`artifacts/windows/VERIFICATION.md`
  - 更新用户说明并记录自动与真机验证。

工作目录：`E:\project\MFA\mfa-verifier\.worktrees\windows-ui-qr-implementation\artifacts\windows`。

任何提交都不得暂存或修改用户已有的 `LiubaiMfa.Windows/Package.appxmanifest` 工作树改动。

### Task 1：用行为测试锁定设置缺省值与启动保护策略

**Files:**
- Create: `LiubaiMfa.Windows/Services/StartupProtectionSettings.cs`
- Create: `LiubaiMfa.Windows/ViewModels/StartupProtectionPolicy.cs`
- Modify: `LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 注册并编写六项失败测试**

在 `Program.cs` 的 `checks` 列表加入：

```csharp
("启动保护设置缺失时默认关闭", () => RunSync(VerifyStartupProtectionDefaultsOff)),
("启动保护设置读取已保存状态", () => RunSync(VerifyStartupProtectionReadsStoredValue)),
("启动保护设置只写入目标布尔值", () => RunSync(VerifyStartupProtectionWritesRequestedValue)),
("启动保护只在开启时阻断启动", () => RunSync(VerifyStartupProtectionOpenPolicy)),
("启动保护切换只在验证和会话有效时提交", () => RunSync(VerifyStartupProtectionTransitionPolicy)),
("启动保护只在开启且已进入主界面时自动锁定", () => RunSync(VerifyStartupProtectionAutoLockPolicy)),
```

在断言辅助方法之前加入：

```csharp
static void VerifyStartupProtectionDefaultsOff()
{
    var settings = new StartupProtectionSettings(_ => null, (_, _) => { });
    Assert(!settings.IsEnabled, "缺少启动保护设置时必须默认关闭。");
}

static void VerifyStartupProtectionReadsStoredValue()
{
    var enabled = new StartupProtectionSettings(_ => true, (_, _) => { });
    var invalid = new StartupProtectionSettings(_ => "true", (_, _) => { });
    Assert(enabled.IsEnabled, "已保存的 true 必须读取为开启。");
    Assert(!invalid.IsEnabled, "非布尔设置不得被解释为开启。");
}

static void VerifyStartupProtectionWritesRequestedValue()
{
    string? writtenKey = null;
    object? writtenValue = null;
    var settings = new StartupProtectionSettings(
        _ => null,
        (key, value) => (writtenKey, writtenValue) = (key, value));

    settings.SetEnabled(true);

    Assert(writtenKey == StartupProtectionSettings.SettingKey, "设置键名不一致。");
    Assert(writtenValue is true, "开启设置必须只写入布尔值 true。");
}

static void VerifyStartupProtectionOpenPolicy()
{
    Assert(!StartupProtectionPolicy.ShouldStartLocked(false), "关闭保护时启动不得进入 Hello 锁定页。");
    Assert(StartupProtectionPolicy.ShouldStartLocked(true), "开启保护时启动必须进入 Hello 锁定页。");
    Assert(!StartupProtectionPolicy.ShouldShowManualLock(false), "关闭保护时不得显示手动锁定入口。");
    Assert(StartupProtectionPolicy.ShouldShowManualLock(true), "开启保护时必须显示手动锁定入口。");
}

static void VerifyStartupProtectionTransitionPolicy()
{
    Assert(StartupProtectionPolicy.CanCommitSettingChange(false, false, false, false), "未改变设置不应要求验证。");
    Assert(!StartupProtectionPolicy.CanCommitSettingChange(false, true, false, true), "开启保护前取消验证不得提交。");
    Assert(!StartupProtectionPolicy.CanCommitSettingChange(true, false, true, false), "旧会话中的成功验证不得提交。");
    Assert(StartupProtectionPolicy.CanCommitSettingChange(false, true, true, true), "有效会话验证成功后应允许开启。");
    Assert(StartupProtectionPolicy.CanCommitSettingChange(true, false, true, true), "有效会话验证成功后应允许关闭。");
}

static void VerifyStartupProtectionAutoLockPolicy()
{
    var delay = TimeSpan.FromMinutes(1);
    Assert(!StartupProtectionPolicy.ShouldScheduleAutoLock(false, true), "关闭保护时失焦不得启动锁定计时。");
    Assert(!StartupProtectionPolicy.ShouldScheduleAutoLock(true, false), "未进入主界面时不得启动锁定计时。");
    Assert(StartupProtectionPolicy.ShouldScheduleAutoLock(true, true), "保护开启且已进入主界面时应启动锁定计时。");
    Assert(!StartupProtectionPolicy.ShouldLockAfterDelay(true, true, delay - TimeSpan.FromSeconds(1), delay), "未达到一分钟不得锁定。");
    Assert(StartupProtectionPolicy.ShouldLockAfterDelay(true, true, delay, delay), "达到一分钟必须锁定。");
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 编译失败，错误指出 `StartupProtectionSettings` 或 `StartupProtectionPolicy` 不存在。

- [ ] **Step 3: 实现最小设置存储**

创建 `StartupProtectionSettings.cs`：

```csharp
using Windows.Storage;

namespace LiubaiMfa.Windows.Services;

public sealed class StartupProtectionSettings
{
    internal const string SettingKey = "Security.UseWindowsHelloOnOpen";
    private readonly Func<string, object?> _read;
    private readonly Action<string, object> _write;

    public StartupProtectionSettings()
        : this(ReadLocalValue, WriteLocalValue)
    {
    }

    internal StartupProtectionSettings(
        Func<string, object?> read,
        Action<string, object> write)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public bool IsEnabled => _read(SettingKey) is true;

    public void SetEnabled(bool enabled) => _write(SettingKey, enabled);

    private static object? ReadLocalValue(string key) =>
        ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var value) ? value : null;

    private static void WriteLocalValue(string key, object value) =>
        ApplicationData.Current.LocalSettings.Values[key] = value;
}
```

- [ ] **Step 4: 实现最小纯策略**

创建 `StartupProtectionPolicy.cs`：

```csharp
namespace LiubaiMfa.Windows.ViewModels;

public static class StartupProtectionPolicy
{
    public static bool ShouldStartLocked(bool enabled) => enabled;

    public static bool ShouldShowManualLock(bool enabled) => enabled;

    public static bool CanCommitSettingChange(
        bool current,
        bool requested,
        bool verificationSucceeded,
        bool sessionCurrent) =>
        requested == current || (verificationSucceeded && sessionCurrent);

    public static bool ShouldScheduleAutoLock(bool enabled, bool isUnlocked) =>
        enabled && isUnlocked;

    public static bool ShouldLockAfterDelay(
        bool enabled,
        bool isUnlocked,
        TimeSpan elapsed,
        TimeSpan delay) =>
        enabled && isUnlocked && elapsed >= delay;
}
```

- [ ] **Step 5: 重新运行回归并确认 GREEN**

Run:

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 六条新增检查输出 `PASS`，全部平台回归退出码为 0。

- [ ] **Step 6: 提交策略与测试**

```powershell
git add -- LiubaiMfa.Windows/Services/StartupProtectionSettings.cs LiubaiMfa.Windows/ViewModels/StartupProtectionPolicy.cs LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: add optional startup protection policy"
```

Expected: 提交仅包含三个目标文件，`Package.appxmanifest` 仍未暂存。

### Task 2：增加设置对话框和主界面入口

**Files:**
- Create: `LiubaiMfa.Windows/Services/ProtectionSettingsDialogService.cs`
- Modify: `LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`

- [ ] **Step 1: 创建只收集选择的设置对话服务**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiubaiMfa.Windows.Services;

public sealed class ProtectionSettingsDialogService
{
    private readonly Func<ContentDialog, long, Task<ContentDialogResult>> _showDialog;
    private readonly Func<long, bool> _isSessionCurrent;

    public ProtectionSettingsDialogService(
        Func<ContentDialog, long, Task<ContentDialogResult>> showDialog,
        Func<long, bool> isSessionCurrent)
    {
        _showDialog = showDialog;
        _isSessionCurrent = isSessionCurrent;
    }

    public async Task<bool?> ShowAsync(XamlRoot root, long token, bool currentValue)
    {
        if (!_isSessionCurrent(token))
        {
            return null;
        }

        var toggle = new ToggleSwitch
        {
            Header = "打开应用时使用 Windows Hello",
            OffContent = "关闭",
            OnContent = "开启",
            IsOn = currentValue,
        };
        var description = new TextBlock
        {
            Text = "开启后，启动应用、手动锁定和离开 1 分钟后需要验证身份。查看密钥和导出备份始终单独验证。",
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "启动保护",
            Content = new StackPanel { Spacing = 12, Children = { toggle, description } },
            PrimaryButtonText = "保存",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await _showDialog(dialog, token);
        return result == ContentDialogResult.Primary && _isSessionCurrent(token)
            ? toggle.IsOn
            : null;
    }
}
```

- [ ] **Step 2: 在现有顶部命令区增加设置按钮**

给当前锁定按钮添加 `x:Name="LockButton"`；在导入二维码和锁定按钮之间加入：

```xml
<Button
    Style="{StaticResource CompactIconButtonStyle}"
    ToolTipService.ToolTip="启动保护设置"
    AutomationProperties.Name="启动保护设置"
    Click="SettingsButton_Click">
    <FontIcon Glyph="&#xE713;" FontSize="16" />
</Button>
```

给底部保护状态文本添加 `x:Name="ProtectionStatusText"`，初始文本改为“本机加密存储”。

- [ ] **Step 3: 暴露事件并集中更新显示状态**

在 `AuthenticatorView.xaml.cs` 加入：

```csharp
public event EventHandler? SettingsRequested;

public void ApplyStartupProtection(bool enabled)
{
    LockButton.Visibility = StartupProtectionPolicy.ShouldShowManualLock(enabled)
        ? Visibility.Visible
        : Visibility.Collapsed;
    ProtectionStatusText.Text = enabled
        ? "本机加密 · Windows Hello 保护"
        : "本机加密存储";
}

private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
    SettingsRequested?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 4: 同步代码检查桩中的具名控件**

在 `GeneratedXamlStubs.cs` 的 `AuthenticatorView` 字段区加入：

```csharp
private readonly Button LockButton = new();
private readonly TextBlock ProtectionStatusText = new();
```

- [ ] **Step 5: 编译 CodeBehindCheck**

Run:

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
```

Expected: 0 警告、0 错误。

- [ ] **Step 6: 提交设置界面外壳**

```powershell
git add -- LiubaiMfa.Windows/Services/ProtectionSettingsDialogService.cs LiubaiMfa.Windows/Views/AuthenticatorView.xaml LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs
git commit -m "feat: add startup protection settings UI"
```

Expected: 只提交实际有差异的目标文件，不包含 `Package.appxmanifest`。

### Task 3：统一直接进入与 Hello 解锁后的会话建立

**Files:**
- Modify: `LiubaiMfa.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: 接入字段、事件和服务**

在 `MainWindow` 加入：

```csharp
private readonly StartupProtectionSettings _startupProtectionSettings;
private bool _startupProtectionEnabled;
```

在构造函数中加入：

```csharp
_startupProtectionSettings = new StartupProtectionSettings();
```

- [ ] **Step 2: 将根视图加载改为按设置分流**

用下列代码替换 `RootGrid_Loaded`：

```csharp
private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
{
    RootGrid.Loaded -= RootGrid_Loaded;
    _startupProtectionEnabled = _startupProtectionSettings.IsEnabled;
    AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);

    if (!StartupProtectionPolicy.ShouldStartLocked(_startupProtectionEnabled))
    {
        try
        {
            await OpenWithoutVerificationAsync();
        }
        catch (Exception)
        {
            ForceLock("无法完成打开操作，请稍后重试。");
        }

        return;
    }

    try
    {
        if (!await _windowsHello.IsAvailableAsync())
        {
            SetHelloUnavailableMessage();
        }
    }
    catch (Exception)
    {
        SetHelloTemporarilyUnavailableMessage();
    }
}
```

- [ ] **Step 3: 提取唯一的数据加载和会话建立方法**

从 `UnlockAsync` 提取：

```csharp
private async Task<bool> EnterMainViewAsync(string loadFailureMessage)
{
    if (!ViewModel.IsLoaded)
    {
        try
        {
            await ViewModel.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            LockView.Message = loadFailureMessage;
            LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
            LockView.Visibility = Visibility.Visible;
            AuthenticatorView.Visibility = Visibility.Collapsed;
            return false;
        }
    }

    var sessionToken = _sessionGuard.BeginUnlockedSession();
    _sessionCancellation.Begin(sessionToken);
    Volatile.Write(ref _activeSession, sessionToken);
    _isUnlocked = true;
    AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
    AuthenticatorView.Attach(ViewModel);
    LockView.ButtonContent = "使用 Windows Hello 解锁";
    LockView.Visibility = Visibility.Collapsed;
    AuthenticatorView.Visibility = Visibility.Visible;
    _timer.Start();
    TryDeliverCancelledBackupNotice(sessionToken);
    return true;
}
```

Hello 验证成功后只调用：

```csharp
await EnterMainViewAsync("无法解锁本地账户数据。为避免覆盖，应用保持锁定。");
```

- [ ] **Step 4: 新增不调用 Hello 的直接进入路径**

```csharp
private async Task OpenWithoutVerificationAsync()
{
    if (_unlockInProgress)
    {
        return;
    }

    _unlockInProgress = true;
    LockView.IsUnlockEnabled = false;
    LockView.Message = "正在打开本机验证码…";
    try
    {
        await EnterMainViewAsync("无法打开本地账户数据。为避免覆盖，应用不会写入新数据。");
    }
    finally
    {
        _unlockInProgress = false;
        LockView.IsUnlockEnabled = true;
    }
}
```

`Unlock_Click` 根据 `_startupProtectionEnabled` 选择 `UnlockAsync` 或 `OpenWithoutVerificationAsync`。异常分支必须调用下一步的同步强制清理方法，不能让旧会话留在内存。

- [ ] **Step 5: 保留原顺序提取强制锁定**

```csharp
private void Lock_Click(object? sender, EventArgs e)
{
    if (_startupProtectionEnabled)
    {
        ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");
    }
}

private void Lock() => ForceLock("使用 Windows Hello 验证身份后即可查看验证码。");

private void ForceLock(string message)
{
    _sessionGuard.Invalidate();
    _sessionCancellation.Invalidate();
    Volatile.Write(ref _activeSession, 0);
    _isUnlocked = false;
    HideActiveDialog();
    _timer.Stop();
    AuthenticatorView.Detach();
    ViewModel.Unload();
    LockView.Message = message;
    LockView.ButtonContent = _startupProtectionEnabled ? "使用 Windows Hello 解锁" : "重试打开";
    AuthenticatorView.Visibility = Visibility.Collapsed;
    LockView.Visibility = Visibility.Visible;
}
```

不得在该方法中增加等待或退场动画。

- [ ] **Step 6: 让失焦锁定服从纯策略**

在 `Window_Activated` 开头加入：

```csharp
if (!StartupProtectionPolicy.ShouldScheduleAutoLock(_startupProtectionEnabled, _isUnlocked))
{
    _backgroundLockCancellation?.Cancel();
    _deactivatedAt = null;
    return;
}
```

重新激活和 `ScheduleBackgroundLockAsync` 延迟完成后的判断都调用：

```csharp
StartupProtectionPolicy.ShouldLockAfterDelay(
    _startupProtectionEnabled,
    _isUnlocked,
    DateTimeOffset.UtcNow - _deactivatedAt.Value,
    BackgroundLockDelay)
```

Expected: 设置关闭后，旧计时器即使迟到也不能锁定。

- [ ] **Step 7: 运行回归和代码编译检查**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
```

Expected: 全部行为检查通过；CodeBehindCheck 0 警告、0 错误。

- [ ] **Step 8: 提交启动分流**

```powershell
git add -- LiubaiMfa.Windows/MainWindow.xaml.cs
git commit -m "feat: make Windows Hello startup protection optional"
```

Expected: `Package.appxmanifest` 未暂存。

### Task 4：实现经验证的设置开启和关闭

**Files:**
- Modify: `LiubaiMfa.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: 实现设置事件编排**

先在 `MainWindow` 字段区加入：

```csharp
private readonly ProtectionSettingsDialogService _protectionSettingsDialogs;
```

在构造函数中加入：

```csharp
AuthenticatorView.SettingsRequested += Settings_Click;
_protectionSettingsDialogs = new ProtectionSettingsDialogService(ShowDialogAsync, IsSessionCurrent);
```

然后加入事件处理器：

```csharp
private async void Settings_Click(object? sender, EventArgs e)
{
    if (!TryCaptureUnlockedSession(out var token))
    {
        return;
    }

    var requested = await _protectionSettingsDialogs.ShowAsync(
        RootGrid.XamlRoot,
        token,
        _startupProtectionEnabled);
    if (requested is null || !IsSessionCurrent(token) || requested.Value == _startupProtectionEnabled)
    {
        return;
    }

    var reason = requested.Value
        ? "验证身份以开启留白 MFA 启动保护"
        : "验证身份以关闭留白 MFA 启动保护";
    var verified = await VerifySensitiveActionAsync(token, reason);
    if (!StartupProtectionPolicy.CanCommitSettingChange(
            _startupProtectionEnabled,
            requested.Value,
            verified,
            IsSessionCurrent(token)))
    {
        return;
    }

    try
    {
        _startupProtectionSettings.SetEnabled(requested.Value);
    }
    catch (Exception)
    {
        ShowStatus(token, "设置未保存，请重试。", InfoBarSeverity.Warning);
        return;
    }

    _startupProtectionEnabled = requested.Value;
    AuthenticatorView.ApplyStartupProtection(_startupProtectionEnabled);
    if (!_startupProtectionEnabled)
    {
        _backgroundLockCancellation?.Cancel();
        _deactivatedAt = null;
    }

    ShowStatus(
        token,
        _startupProtectionEnabled ? "启动保护已开启。" : "启动保护已关闭。",
        InfoBarSeverity.Success);
}
```

- [ ] **Step 2: 核对不可颠倒的写入顺序**

顺序必须是：

```text
当前会话 → 用户选择 → Windows Hello → 再次核对会话 → 写入设置 → 更新内存和界面
```

Expected: 取消、失败、异常或旧会话不会写设置，也不会只改变开关外观。

- [ ] **Step 3: 运行平台回归、CodeBehindCheck 和 Release 构建**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

Expected: 全部命令退出码为 0；Release 构建 0 警告、0 错误。

- [ ] **Step 4: 提交设置切换编排**

```powershell
git add -- LiubaiMfa.Windows/MainWindow.xaml.cs
git commit -m "feat: verify startup protection setting changes"
```

### Task 5：更新说明并完成真实启动验证

**Files:**
- Modify: `README.md`
- Modify: `VERIFICATION.md`

- [ ] **Step 1: 更新 README**

将已实现范围改为：

```markdown
- 可选的 Windows Hello 启动保护；开启后支持启动解锁、手动锁定和离开 60 秒后自动锁定。
- 查看/复制 MFA 原始密钥和导出备份前始终再次进行 Windows Hello 验证。
```

在安全说明补充：

```markdown
“打开应用时使用 Windows Hello”默认关闭。关闭时应用仍使用当前 Windows 用户范围的 DPAPI 设备密钥和加密账户文件，但打开主界面前不弹系统验证。
```

- [ ] **Step 2: 运行完整自动验证**

```powershell
dotnet restore .\LiubaiMfa.sln
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

Expected: 两组回归全部通过；两个构建均为 0 警告、0 错误。

- [ ] **Step 3: 构建、安装并启动真实 MSIX**

```powershell
$mfaCertificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=LiubaiMFA-Development' | Select-Object -First 1
if ($null -eq $mfaCertificate) { throw '缺少 Liubai MFA 开发证书' }
.\scripts\build-msix.ps1 -CertificateThumbprint $mfaCertificate.Thumbprint
$mfaPackage = Get-ChildItem -LiteralPath .\AppPackages -Recurse -Filter *.msix | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $mfaPackage) { throw '未生成 MSIX' }
Add-AppxPackage -ForceApplicationShutdown -Path $mfaPackage.FullName
$installedMfa = Get-AppxPackage -Name 'com.mfaverifier.app.windows'
if ($null -eq $installedMfa) { throw 'MSIX 未安装' }
Start-Process "shell:AppsFolder\$($installedMfa.PackageFamilyName)!App"
```

Expected: MSIX 生成并安装成功，留白 MFA 可见窗口启动。

- [ ] **Step 4: 完成六条真机路径**

1. 设置缺省/关闭：结束进程后重新打开，直接出现主界面，不弹 Windows Hello。
2. 点击“添加账户”：直接出现添加对话框，不弹 Windows Hello；取消，不保存测试数据。
3. 开启保护并取消系统验证：设置保持关闭。
4. 开启保护并验证成功：设置显示开启；重启后先显示锁定页并要求 Hello。
5. 关闭保护并取消验证：设置保持开启；验证成功后才真正关闭。
6. 关闭保护时离开超过 60 秒不锁定；重新开启后离开超过 60 秒必须锁定并立即隐藏账户内容。

只使用测试账户，不在记录、截图或聊天中暴露真实验证码、MFA 密钥或恢复密钥。

- [ ] **Step 5: 更新 VERIFICATION.md 并提交**

把每条路径写成“通过 / 未通过 / 受阻”，附命令和非敏感结果；未实际点击完成的 Hello 路径不得写成通过。然后执行：

```powershell
git add -- README.md VERIFICATION.md
git commit -m "docs: document optional startup protection"
```

Expected: 文档提交完成，`Package.appxmanifest` 保持用户原有未提交状态，最终验证过的留白 MFA 窗口继续运行。
