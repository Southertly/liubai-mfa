# Windows MFA 二级页面分组列表 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将添加账户、设置、锁定三个 WinUI 二级页面改造成参考图风格的紧凑分组列表，同时保留现有功能、导航和安全逻辑。

**Architecture:** 继续使用现有三个 UserControl 和 code-behind 事件，不引入新组件库或数据层。共享尺寸、圆角和主题资源放入 `App.xaml`，页面使用单个分组 Border 包含多个透明行按钮，行内统一图标槽、文字层级和右侧状态/箭头。

**Tech Stack:** WinUI 3, C#, XAML, .NET 8, Windows App SDK, MSIX。

---

### Task 1: 建立分组列表共享资源

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml`

- [ ] **Step 1: Add shared grouped-layout tokens**

在现有 `MfaSecondaryContentWidth` 附近增加最大内容宽度、分组圆角、单行高度和带说明行高度资源。使用现有主题画刷，不新增硬编码的浅色专用画刷：

```xml
<x:Double x:Key="MfaSecondaryContentMaxWidth">680</x:Double>
<CornerRadius x:Key="MfaGroupCornerRadius">14</CornerRadius>
<x:Double x:Key="MfaGroupRowHeight">56</x:Double>
<x:Double x:Key="MfaGroupDetailRowHeight">68</x:Double>
```

- [ ] **Step 2: Parse the resource dictionary**

运行：

```powershell
[xml](Get-Content -LiteralPath 'artifacts/windows/LiubaiMfa.Windows/App.xaml' -Raw) | Out-Null
```

预期：命令成功返回，不出现 XML 解析错误。

---

### Task 2: 重做设置页为两组列表面板

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/SettingsView.xaml`
- Preserve: `artifacts/windows/LiubaiMfa.Windows/Views/SettingsView.xaml.cs`

- [ ] **Step 1: Replace fixed narrow-column layout with a centered responsive content column**

保留 `InfoBar`、返回按钮、`BackButton` 名称和现有标题状态文案。将页面内容容器设置为 `HorizontalAlignment="Stretch"`，内部内容最大宽度使用 `MfaSecondaryContentMaxWidth`，让宽窗口居中、窄窗口收缩。

- [ ] **Step 2: Build the 安全 group panel**

使用一个 `Border` 作为分组面板，设置 `Background="{ThemeResource MfaRaisedSurfaceBrush}"`、主题边框、`CornerRadius="{StaticResource MfaGroupCornerRadius}"`。放入两个透明 `Button` 行，分别保留 `LockButton`、`StartupProtectionButton` 名称和现有 Click 处理器。第一行左侧使用锁定图标，右侧显示“锁定”；第二行左侧使用保护图标，右侧绑定现有 `StartupProtectionStatusText`。两行之间添加内缩的 `Border Height="1"` 分隔线。

- [ ] **Step 3: Build the 数据 group panel**

用同一面板结构承载导入和导出按钮，保留 `ImportBackupButton_Click`、`ExportBackupButton_Click` 和现有自动化名称。右侧分别显示“选择文件”和“导出”，带说明的行高使用 `MfaGroupDetailRowHeight`，两行之间使用相同内缩分隔线。

- [ ] **Step 4: Keep local-only notice as plain footer text**

保留本地保存和恢复密码提示，放在数据面板下方，不再包在单独 Border 中。所有行标题使用 `Microsoft YaHei UI` 的 16 DIP，分组标题 13 DIP，说明文字 12–13 DIP。

- [ ] **Step 5: Parse the page XAML**

运行：

```powershell
[xml](Get-Content -LiteralPath 'artifacts/windows/LiubaiMfa.Windows/Views/SettingsView.xaml' -Raw) | Out-Null
```

预期：成功解析。

---

### Task 3: 重做添加账户页为推荐/其他方式列表

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml`
- Preserve: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml.cs`

- [ ] **Step 1: Keep header, status bar and keyboard back handling**

保留 `StatusBar`、返回按钮、`BackButton_Click` 和 `AddAccountView_KeyDown`。内容容器使用与设置页相同的最大宽度和居中规则。

- [ ] **Step 2: Replace the large blue scan hero with the 推荐方式 group**

移除现有大面积蓝色 `ScreenshotScanButton` 外观，保留同名按钮和 `ScreenshotScanButton_Click`。将它放入浅色圆角分组面板，作为一行带图标、标题“截图并扫描二维码”、说明和右侧 `ChevronRight` 的可点击行。

- [ ] **Step 3: Build the 其他方式 group**

在第二个分组面板中放入 `ImportImageButton` 和 `ManualEntryButton`，保留原有点击事件、AutomationProperties 和帮助文案。两行统一高度、图标槽和分隔线，右侧使用统一箭头。

- [ ] **Step 4: Parse the page XAML**

运行：

```powershell
[xml](Get-Content -LiteralPath 'artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml' -Raw) | Out-Null
```

预期：成功解析。

---

### Task 4: 重做锁定页为安全操作面板

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml.cs`

- [ ] **Step 1: Keep centered security introduction**

保留锁定图标、`LockMessage`、标题和“本地优先”说明，使用更紧凑的桌面间距和 28 DIP 页面标题。

- [ ] **Step 2: Put both unlock actions in one grouped panel**

保留 `UnlockButton`、`PasswordUnlockButton` 以及两个 Click 处理器。将两者改成同一面板内的透明行按钮：Windows Hello 行使用强调色图标和主操作状态，电脑密码行使用普通图标；行间添加内缩分隔线。新增命名的行内文字控件，让 code-behind 的 `ButtonContent`、`ShowHelloPrimary`、`ShowPasswordPrimary` 更新文字而不覆盖整行 Grid 内容；保留第二行可见性控制。

- [ ] **Step 3: Parse the page XAML**

运行：

```powershell
[xml](Get-Content -LiteralPath 'artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml' -Raw) | Out-Null
```

预期：成功解析。

---

### Task 5: 构建、测试、打包并启动验证

**Files:**
- Verify: `artifacts/windows/LiubaiMfa.sln`
- Verify: `artifacts/windows/scripts/build-msix.ps1`
- Temporary modify/restore: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`

- [ ] **Step 1: Stop the running packaged app before rebuilding**

运行：

```powershell
Stop-Process -Name 'LiubaiMfa.Windows' -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 2: Build and test the Windows solution**

运行：

```powershell
dotnet build 'artifacts/windows/LiubaiMfa.sln' -c Release -p:Platform=x64 --no-restore
dotnet test 'artifacts/windows/LiubaiMfa.sln' -c Release -p:Platform=x64 --no-restore
```

预期：两个命令退出码为 0。

- [ ] **Step 3: Run the existing Windows core regression and build a temporary MSIX**

将 `Package.appxmanifest` 版本临时改为 `1.0.0.12`，执行：

```powershell
& '.\artifacts\windows\scripts\build-msix.ps1' -CertificateThumbprint '5B5E73E16D75BA1B0B51DE8CF26A4A1CB581B6D6'
```

预期：5 项 Windows 核心安全回归通过、MSIX 生成成功；`mspdbcmf.exe` 符号包警告可存在但不应有错误。生成后把清单版本恢复为 `1.0.0.7`。

- [ ] **Step 4: Install and launch the temporary package**

运行：

```powershell
$pkg = 'artifacts/windows/AppPackages/LiubaiMfa.Windows_1.0.0.12_x64_Test/LiubaiMfa.Windows_1.0.0.12_x64.msix'
Add-AppxPackage -Path $pkg -ForceApplicationShutdown
$app = Get-AppxPackage | Where-Object Name -eq 'com.mfaverifier.app.windows'
$exe = Join-Path $app.InstallLocation 'LiubaiMfa.Windows.exe'
Start-Process -FilePath $exe | Out-Null
```

- [ ] **Step 5: Verify objective startup signals and leave the app running**

运行：

```powershell
Start-Sleep -Seconds 4
$app = Get-AppxPackage | Where-Object Name -eq 'com.mfaverifier.app.windows'
$proc = Get-Process -Name 'LiubaiMfa.Windows'
[pscustomobject]@{Version=$app.Version; Title=$proc.MainWindowTitle; Responding=$proc.Responding}
```

预期：版本为 `1.0.0.12`、标题为“留白 MFA”、`Responding` 为 `True`。保留该运行实例供用户检查页面。

- [ ] **Step 6: Review final diff and workspace status**

运行：

```powershell
git diff --check
git status --short
```

预期：无空白错误；仅报告本次页面、资源和文档相关改动，不回滚用户已有修改。
