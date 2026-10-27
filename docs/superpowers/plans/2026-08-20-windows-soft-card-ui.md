# Windows 柔和卡片 UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将留白 MFA Windows 主界面、锁定页和账户对话框精修为适合 10～30 个账户的原生柔和卡片界面，并保持高频操作、安全清理和可访问性不退化。

**Architecture:** 视觉常量集中在 `App.xaml`，页面继续使用 WinUI 原生控件和系统主题资源；账户卡片仍由现有 ViewModel 驱动，只增加可测试的临期状态。窗口尺寸、Mica 外壳、主列表、锁定页和对话框分阶段落地，每阶段都先跑现有回归和编译检查。

**Tech Stack:** WinUI 3 XAML、C# 12、.NET 8、Windows App SDK 2.3.1、Fluent ThemeResources、Mica、现有 Core/Platform Regression 与 MSIX 工具链

---

## 文件结构与职责

- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml`
  - 集中页面间距、卡片圆角、卡片容器和紧凑按钮样式。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml`、`MainWindow.xaml.cs`
  - 提供透明内容层和 Windows 11 Mica 外壳。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/WindowPlacementService.cs`
  - 将默认和最小窗口尺寸调整到柔和卡片布局。
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`
  - 提供最后 5 秒临期状态，不引入 UI 类型。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
  - 重排顶部、搜索、分组、账户卡片、空状态和底部状态区。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
  - 只补充必要的视图状态，不改变搜索、复制、定位和清理行为。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml`、`LockView.xaml.cs`
  - 统一柔和卡片视觉，继续支持动态标题、消息和主按钮状态。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/AccountDialogService.cs`
  - 统一字段间距，并让新密钥输入默认遮挡。
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
  - 测试窗口尺寸和验证码临期状态。
- Modify: `artifacts/windows/VERIFICATION.md`
  - 记录主题、输入方式、窗口尺寸和真机视觉验收。

工作目录：`E:\project\MFA\mfa-verifier\.worktrees\windows-ui-qr-implementation\artifacts\windows`。

前置条件：先完成 `2026-08-20-windows-optional-startup-protection.md`。所有提交继续排除用户已有的 `LiubaiMfa.Windows/Package.appxmanifest` 改动。

## Before / After Review

| 维度 | 当前界面 | 本计划完成后 |
|---|---|---|
| 视觉层级 | 标题、命令、账户和底部状态的权重接近 | 验证码最高，服务/账户次之，保护状态保持安静 |
| 空间与形状 | 账户行偏薄、控件之间像独立拼接 | 84～92px 柔和卡片、统一 12px 圆角与 8px 间距 |
| 平台感 | 能使用但精修不足 | 使用 Mica、ThemeResource 和原生控件状态，保持 Windows 11 感 |
| 高频操作 | 复制路径正确，但按钮与代码区域关系偏生硬 | 点击代码或复制按钮立即复制，反馈克制且不增加动画等待 |
| 动效 | 主要依赖默认控件反馈 | 只保留原生悬停/按下状态；搜索、计时和锁定不做装饰性动画 |
| 安全表达 | 底部固定显示 Windows Hello 保护 | 根据真实设置显示“本机加密存储”或“Windows Hello 保护” |

### Task 1：用测试锁定临期状态和新窗口尺寸

**Files:**
- Modify: `LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`
- Modify: `LiubaiMfa.Windows/Services/WindowPlacementService.cs`
- Modify: `LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 注册临期状态与窗口规格测试**

在 `checks` 列表加入：

```csharp
("验证码最后五秒进入临期状态", () => RunSync(VerifyAccountExpiryState)),
("柔和卡片窗口使用新默认和最小尺寸", () => RunSync(VerifySoftCardWindowDimensions)),
```

加入测试：

```csharp
static void VerifyAccountExpiryState()
{
    Assert(!AccountItemViewModel.IsExpiringAt(30), "完整周期不得显示临期。");
    Assert(!AccountItemViewModel.IsExpiringAt(6), "剩余 6 秒不得显示临期。");
    Assert(AccountItemViewModel.IsExpiringAt(5), "剩余 5 秒必须进入临期。");
    Assert(AccountItemViewModel.IsExpiringAt(1), "剩余 1 秒必须保持临期。");
    Assert(!AccountItemViewModel.IsExpiringAt(0), "无有效验证码时不得显示临期。");
}

static void VerifySoftCardWindowDimensions()
{
    Assert(WindowPlacementService.DefaultEffectiveWidth == 560, "默认宽度必须为 560。");
    Assert(WindowPlacementService.DefaultEffectiveHeight == 780, "默认高度必须为 780。");
    Assert(WindowPlacementService.MinimumEffectiveWidth == 480, "最小宽度必须为 480。");
    Assert(WindowPlacementService.MinimumEffectiveHeight == 620, "最小高度必须为 620。");
}
```

同时把现有窗口测试中的最小尺寸参数从 `460, 560` 改为 `480, 620`；损坏尺寸期望改为 `new PixelRect(0, 0, 480, 620)`，极端损坏期望改为 `new PixelRect(0, 420, 480, 620)`。

- [ ] **Step 2: 运行测试确认 RED**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 因 `IsExpiringAt` 不存在或窗口常量仍为旧值而失败。

- [ ] **Step 3: 实现临期状态与通知**

在 `AccountItemViewModel` 加入：

```csharp
public bool IsExpiring => IsExpiringAt(SecondsRemaining);

internal static bool IsExpiringAt(int secondsRemaining) =>
    secondsRemaining is > 0 and <= 5;
```

在 `SecondsRemaining` 成功变更时补充：

```csharp
OnPropertyChanged(nameof(IsExpiring));
```

在现有“刷新和替换通知”测试中加入：

```csharp
Assert(changes.Contains(nameof(AccountItemViewModel.IsExpiring)), "秒数变化必须通知临期状态。");
```

- [ ] **Step 4: 更新窗口常量**

在 `WindowPlacementService` 中改为：

```csharp
internal const int DefaultEffectiveWidth = 560;
internal const int DefaultEffectiveHeight = 780;
internal const int MinimumEffectiveWidth = 480;
internal const int MinimumEffectiveHeight = 620;
```

不改动 DPI 换算、屏幕边界限制和用户已保存窗口位置的恢复逻辑。

- [ ] **Step 5: 运行回归确认 GREEN**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 两条新增检查和所有既有平台回归通过。

- [ ] **Step 6: 提交视图模型与窗口规格**

```powershell
git add -- LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs LiubaiMfa.Windows/Services/WindowPlacementService.cs LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: prepare soft card account presentation"
```

Expected: `Package.appxmanifest` 未暂存。

### Task 2：建立集中 Fluent 资源和 Mica 外壳

**Files:**
- Modify: `LiubaiMfa.Windows/App.xaml`
- Modify: `LiubaiMfa.Windows/MainWindow.xaml`
- Modify: `LiubaiMfa.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: 扩展集中样式资源**

保留 `XamlControlsResources`，将应用资源扩展为：

```xml
<Thickness x:Key="MfaPagePadding">24,20,24,16</Thickness>
<Thickness x:Key="MfaCardPadding">14,12</Thickness>
<CornerRadius x:Key="MfaCardCornerRadius">12</CornerRadius>
<x:Double x:Key="MfaCardSpacing">8</x:Double>

<Style x:Key="CompactIconButtonStyle" TargetType="Button">
    <Setter Property="Width" Value="36" />
    <Setter Property="Height" Value="36" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
</Style>

<Style x:Key="MfaAccountItemStyle" TargetType="ListViewItem">
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
    <Setter Property="MinHeight" Value="84" />
    <Setter Property="Padding" Value="{StaticResource MfaCardPadding}" />
    <Setter Property="Margin" Value="0,0,0,8" />
    <Setter Property="CornerRadius" Value="{StaticResource MfaCardCornerRadius}" />
    <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
    <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
    <Setter Property="BorderThickness" Value="1" />
</Style>
```

不创建完整控件模板，不在页面散落固定浅色/深色背景。

- [ ] **Step 2: 让根内容层支持 Mica**

将 `MainWindow.xaml` 的根背景改为透明：

```xml
<Grid
    x:Name="RootGrid"
    Background="Transparent">
```

在 `MainWindow.xaml.cs` 增加：

```csharp
using Microsoft.UI.Xaml.Media;
```

在 `InitializeComponent();` 后设置：

```csharp
SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
```

Windows 11 x64 是项目目标平台，不增加自绘背景或第三方材质。

- [ ] **Step 3: 构建 WinUI 项目验证资源名和 Mica API**

```powershell
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
```

Expected: XAML 和 C# 编译成功，0 警告、0 错误。若 SDK 对 `MicaKind.BaseAlt` 的 API 形式不同，查本机 2.3.1 引用后只调整该初始化语句，不改设计目标。

- [ ] **Step 4: 提交资源和外壳**

```powershell
git add -- LiubaiMfa.Windows/App.xaml LiubaiMfa.Windows/MainWindow.xaml LiubaiMfa.Windows/MainWindow.xaml.cs
git commit -m "style: add Fluent resources and Mica shell"
```

### Task 3：重排主界面和柔和账户卡片

**Files:**
- Modify: `LiubaiMfa.Windows/Views/AuthenticatorView.xaml`

- [ ] **Step 1: 重建页面外层间距和顶部层级**

把根网格改为：

```xml
<Grid Padding="{StaticResource MfaPagePadding}" RowSpacing="12">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>
```

顶部使用两行结构：第一行保留 `StatusBar`；第二行标题左对齐，保护状态使用次级文字，右侧依次排列导入、设置、条件显示的锁定和强调色添加按钮。标题使用 `28` 号半粗体，说明文字使用 `12` 号次级色。按钮继续提供中文 ToolTip 和 AutomationProperties.Name。

- [ ] **Step 2: 精修搜索与分组**

搜索框保持全宽，使用：

```xml
<AutoSuggestBox
    x:Name="SearchBox"
    Grid.Row="1"
    Height="40"
    PlaceholderText="搜索服务、账户或分组"
    QueryIcon="Find"
    AutomationProperties.Name="搜索账户"
    TextChanged="SearchBox_TextChanged" />
```

分组列表保持横向滚动；项目容器设置为最小高度 32、圆角 16、左右内边距 12、项目间距 6。继续使用现有 `GroupFilterItemViewModel.DisplayText`，不创建新的导航页。

- [ ] **Step 3: 用柔和卡片替换当前账户项容器**

`AccountsList` 使用 `ItemContainerStyle="{StaticResource MfaAccountItemStyle}"`，继续设置 `SelectionMode="None"` 和现有可访问名称。卡片内容替换为以下结构；`SwipeControl`、右滑操作和上下文菜单保持原事件：

```xml
<Border
    Tag="{x:Bind Id}"
    Background="Transparent"
    IsTabStop="True"
    UseSystemFocusVisuals="True"
    AutomationProperties.Name="{x:Bind AccessibleName, Mode=OneWay}"
    KeyDown="AccountRow_KeyDown">
<Grid RowSpacing="9">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>
    <Grid ColumnSpacing="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="40" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="36" />
        </Grid.ColumnDefinitions>

        <Border
            Width="40"
            Height="40"
            Background="{x:Bind Icon.BackgroundColor}"
            CornerRadius="10"
            HighContrastAdjustment="Auto"
            AutomationProperties.Name="服务图标">
            <TextBlock
                Text="{x:Bind Icon.FallbackText}"
                Foreground="{x:Bind Icon.ForegroundColor}"
                FontSize="15"
                FontWeight="SemiBold"
                HorizontalAlignment="Center"
                VerticalAlignment="Center" />
        </Border>

        <StackPanel Grid.Column="1" Spacing="3" VerticalAlignment="Center">
            <TextBlock
                Text="{x:Bind Issuer}"
                FontSize="15"
                FontWeight="SemiBold"
                Foreground="{ThemeResource TextFillColorPrimaryBrush}"
                MaxLines="1"
                TextTrimming="CharacterEllipsis" />
            <Grid ColumnSpacing="6">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock
                    Text="{x:Bind Account}"
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                    MaxLines="1"
                    TextTrimming="CharacterEllipsis" />
                <TextBlock
                    Grid.Column="1"
                    Text="{x:Bind GroupLabel}"
                    FontSize="11"
                    MaxWidth="72"
                    Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                    MaxLines="1"
                    TextTrimming="CharacterEllipsis" />
            </Grid>
        </StackPanel>

        <StackPanel Grid.Column="2" MinWidth="118" Spacing="2" VerticalAlignment="Center">
            <Button
                Padding="0"
                Tag="{x:Bind Id}"
                Background="Transparent"
                BorderThickness="0"
                HorizontalAlignment="Right"
                IsTabStop="False"
                ToolTipService.ToolTip="复制验证码"
                AutomationProperties.Name="复制验证码"
                Click="CopyCodeButton_Click">
                <TextBlock
                    Text="{x:Bind FormattedCode, Mode=OneWay}"
                    FontFamily="Consolas"
                    FontSize="24"
                    FontWeight="SemiBold"
                    Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
            </Button>
            <TextBlock
                Text="{x:Bind RemainingLabel, Mode=OneWay}"
                FontSize="11"
                Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                HorizontalAlignment="Right" />
        </StackPanel>

        <Button
            Grid.Column="3"
            Style="{StaticResource CompactIconButtonStyle}"
            Tag="{x:Bind Id}"
            IsTabStop="False"
            ToolTipService.ToolTip="复制验证码"
            AutomationProperties.Name="复制验证码"
            Click="CopyCodeButton_Click">
            <FontIcon Glyph="&#xE8C8;" FontSize="14" />
        </Button>
    </Grid>

    <ProgressBar
        x:Name="ExpiryProgress"
        Grid.Row="1"
        Height="3"
        Maximum="1"
        Value="{x:Bind ProgressValue, Mode=OneWay}"
        Foreground="{ThemeResource AccentFillColorDefaultBrush}"
        Background="{ThemeResource ControlStrongFillColorDefaultBrush}" />

    <VisualStateManager.VisualStateGroups>
        <VisualStateGroup x:Name="ExpiryStates">
            <VisualState x:Name="NormalExpiry" />
            <VisualState x:Name="Expiring">
                <VisualState.StateTriggers>
                    <StateTrigger IsActive="{x:Bind IsExpiring, Mode=OneWay}" />
                </VisualState.StateTriggers>
                <VisualState.Setters>
                    <Setter Target="ExpiryProgress.Foreground" Value="{ThemeResource SystemFillColorCriticalBrush}" />
                </VisualState.Setters>
            </VisualState>
        </VisualStateGroup>
    </VisualStateManager.VisualStateGroups>
</Grid>
</Border>
```

把原有 `ContextFlyout`、`Tag`、`AutomationProperties.Name`、`KeyDown` 和高亮边界迁移到新卡片根元素，不能删除编辑、查看密钥、删除、Ctrl+C 或新增账户定位行为。

- [ ] **Step 4: 精修空状态与底部状态区**

无账户状态使用系统账户/添加图标、标题“尚未添加账户”、一句说明和“添加账户”“导入二维码”按钮。无结果状态保留“清除搜索和筛选”。底部状态区使用 12 号次级文字和普通文本按钮“备份与恢复”，不增加装饰性分隔线或渐变。

- [ ] **Step 5: 编译并运行回归**

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
```

Expected: 代码检查、平台回归和真实 XAML 编译全部通过，0 警告、0 错误。

- [ ] **Step 6: 提交主界面**

```powershell
git add -- LiubaiMfa.Windows/Views/AuthenticatorView.xaml
git commit -m "style: redesign authenticator with soft cards"
```

### Task 4：统一锁定页和账户对话框

**Files:**
- Modify: `LiubaiMfa.Windows/Views/LockView.xaml`
- Modify: `LiubaiMfa.Windows/Services/AccountDialogService.cs`

- [ ] **Step 1: 将锁定内容放入柔和卡片**

把锁定页中心内容改为：

```xml
<Border
    Width="380"
    Padding="28"
    Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
    BorderThickness="1"
    CornerRadius="16"
    HorizontalAlignment="Center"
    VerticalAlignment="Center">
    <StackPanel Spacing="16">
        <Border
            Width="56"
            Height="56"
            Background="{ThemeResource AccentFillColorSecondaryBrush}"
            CornerRadius="16"
            HorizontalAlignment="Center">
            <FontIcon
                Glyph="&#xE72E;"
                FontSize="24"
                Foreground="{ThemeResource TextOnAccentFillColorPrimaryBrush}"
                HorizontalAlignment="Center"
                VerticalAlignment="Center"
                AutomationProperties.Name="锁定" />
        </Border>
        <TextBlock
            Text="验证码已锁定"
            FontSize="26"
            FontWeight="SemiBold"
            TextAlignment="Center"
            Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
        <TextBlock
            x:Name="LockMessage"
            Text="使用 Windows Hello 验证身份后即可查看验证码。"
            Foreground="{ThemeResource TextFillColorSecondaryBrush}"
            TextAlignment="Center"
            TextWrapping="Wrap" />
        <Button
            x:Name="UnlockButton"
            Content="使用 Windows Hello 解锁"
            Style="{StaticResource AccentButtonStyle}"
            HorizontalAlignment="Stretch"
            AutomationProperties.Name="使用 Windows Hello 解锁"
            Click="UnlockButton_Click" />
        <TextBlock
            Text="离开 1 分钟后自动重新锁定"
            FontSize="12"
            Foreground="{ThemeResource TextFillColorTertiaryBrush}"
            HorizontalAlignment="Center" />
    </StackPanel>
</Border>
```

锁定时仍由 `MainWindow.ForceLock` 先清会话和 ViewModel，再显示此视图；不得加入退场动画。

- [ ] **Step 2: 统一添加和编辑对话框间距**

将 `AccountDialogService` 中表单 `StackPanel.Spacing` 从 10 调整为 12。为服务、账户和分组字段保留现有中文 Header、Placeholder 和验证逻辑。

- [ ] **Step 3: 让新密钥输入默认遮挡**

把 `ShowDraftAsync` 中的密钥控件改为：

```csharp
var secretBox = new PasswordBox
{
    Header = "Base32 密钥或 otpauth:// 地址",
    Password = initial?.Secret ?? string.Empty,
    PlaceholderText = "粘贴密钥或 otpauth:// 地址",
    PasswordRevealMode = PasswordRevealMode.Peek,
};
```

创建草稿时将 `secretBox.Text` 改为 `secretBox.Password`。对话框结束后在 `finally` 中执行：

```csharp
secretBox.Password = string.Empty;
```

现有 TOTP 格式校验和错误文案保持不变。

- [ ] **Step 4: 运行 CodeBehindCheck、平台回归和 WinUI 构建**

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
```

Expected: 三条命令退出码均为 0，XAML 编译无资源或控件属性错误。

- [ ] **Step 5: 提交锁定页和对话框**

```powershell
git add -- LiubaiMfa.Windows/Views/LockView.xaml LiubaiMfa.Windows/Services/AccountDialogService.cs
git commit -m "style: polish lock and account dialogs"
```

### Task 5：执行动效、可访问性和安全边界审查

**Files:**
- Review: `LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Review: `LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Review: `LiubaiMfa.Windows/Views/LockView.xaml`
- Review: `LiubaiMfa.Windows/Services/AccountDialogService.cs`

- [ ] **Step 1: 检查高频操作没有自定义延迟动效**

逐项确认：

- 复制验证码先调用剪贴板，再显示状态；没有缩放、弹跳或延时。
- 搜索和分组切换直接刷新，不做逐项入场。
- 每秒验证码更新不启动布局动画。
- `ForceLock` 不等待动画完成。
- 卡片悬停只使用 WinUI 原生 `ListViewItem` 状态。
- 系统关闭动画时界面仍完整可用。

Expected: 不需要新增自定义 `Storyboard`。若发现会阻塞操作的动画，删除该动画而不是缩短它。

- [ ] **Step 2: 检查无障碍与主题资源**

逐项确认：

- 所有图标按钮都有中文 ToolTip 和 `AutomationProperties.Name`。
- 卡片可通过 Tab 聚焦，`Ctrl+C` 仍只复制当前卡片。
- 临期状态同时保留“剩余 N 秒”文本，不只靠红色。
- 高对比度下服务图标启用 `HighContrastAdjustment="Auto"`。
- 页面颜色全部来自 ThemeResource 或集中资源；不新增页面级固定背景色。
- 最小窗口 480×620 时添加、设置、搜索、分组和备份入口都可达。

- [ ] **Step 3: 核对敏感信息清理和剪贴板策略**

Run:

```powershell
rg -n "Clipboard\.SetContent|Clipboard\.Flush" .\LiubaiMfa.Windows -g "*.cs"
```

Expected: 直接系统剪贴板调用只存在于 `ClipboardService`。随后运行：

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 验证码、MFA 密钥和恢复密钥失败策略检查全部通过；锁定会话和定位代际检查全部通过。

- [ ] **Step 4: 提交审查中发现的必要修正**

仅当本 Task 实际修改文件时执行：

```powershell
git add -- LiubaiMfa.Windows/Views/AuthenticatorView.xaml LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs LiubaiMfa.Windows/Views/LockView.xaml LiubaiMfa.Windows/Services/AccountDialogService.cs
git commit -m "fix: preserve accessible secure UI behavior"
```

Expected: 如果没有差异则不创建空提交。

### Task 6：完整构建、真实视觉验收并留应用运行

**Files:**
- Modify: `VERIFICATION.md`

- [ ] **Step 1: 运行完整自动验证**

```powershell
dotnet restore .\LiubaiMfa.sln
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

Expected: Core 和 Platform 回归全部通过；两个构建 0 警告、0 错误。

- [ ] **Step 2: 构建、安装并启动最新 MSIX**

沿用可选启动保护计划中已验证的开发证书和 MSIX 命令，安装最新包后启动 `com.mfaverifier.app.windows`。

Expected: 默认约 560×780 的留白 MFA 可见窗口启动；最终验收完成前不结束该实例。

- [ ] **Step 3: 检查浅色、深色和高对比度**

每种主题检查：

1. Mica/退回背景与卡片层级清晰。
2. 文字、描边、图标、搜索、分组和验证码可读。
3. 服务图标在高对比度下不丢失。
4. 临期进度条仍有剩余秒数文本。
5. 内容没有硬编码颜色造成的亮块或黑块。

不在聊天或验证文档中附带含真实验证码、MFA 密钥或恢复密钥的截图。

- [ ] **Step 4: 检查尺寸和输入方式**

- 默认窗口可看到约 5～6 张完整卡片；10～30 个账户时滚动、搜索和分组无明显卡顿。
- 缩小到 480×620 时核心入口仍可见或可滚动到达。
- 鼠标悬停和按下反馈克制；复制立即发生。
- Tab、Shift+Tab、Enter、Space、方向键和当前卡片 `Ctrl+C` 可用。
- 触摸/触控板滑动仍可进入编辑、查看密钥和删除。
- 添加账户的密钥默认遮挡，可主动短暂显示。

- [ ] **Step 5: 检查安全与启动保护组合**

- 启动保护关闭：打开应用直接进入柔和卡片主界面。
- 启动保护开启：锁定页使用新视觉，验证成功后进入同一主界面。
- 点击立即锁定：验证码和账户元数据立即消失，无退场延迟。
- 添加账户不弹 Hello；查看密钥和导出备份仍弹 Hello。
- 剪贴板失败只提示一次，不自动重试，不显示错误的成功状态。

- [ ] **Step 6: 更新验证记录并提交**

在 `VERIFICATION.md` 写入本次日期、自动命令结果、三种主题、两种窗口尺寸、输入方式和启动保护组合的“通过 / 未通过 / 受阻”。未实际完成的人工路径必须标为受阻。

```powershell
git add -- VERIFICATION.md
git commit -m "docs: record soft card UI verification"
git status --short
```

Expected: 验证记录提交完成；状态输出只保留用户原有 `Package.appxmanifest` 改动；最终通过检查的留白 MFA 应用继续运行，供用户直接体验。
