# Windows 紧凑界面重设计实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 把留白 MFA Windows 客户端改造成默认约 `520 × 680`、首屏完整显示 6 个账户、支持搜索/分组/左滑操作的 Windows 11 紧凑工具窗口，同时保留现有 Windows Hello、DPAPI 和加密备份能力。

**Architecture:** 保持 `LiubaiMfa.Core` 负责 MFA 核心数据和算法，`LiubaiMfa.Windows.Platform` 负责可独立编译的仓库与 ViewModel，`LiubaiMfa.Windows` 只承载 WinUI 视图和 Windows 生命周期。新增纯逻辑查询、窗口边界计算和平台回归项目；把 `MainWindow` 收缩为窗口/锁定/导航编排器，把验证码主视图和对话流程拆出。所有用户可见文案使用简体中文，所有颜色取自 WinUI 主题资源。

**Tech Stack:** C#、.NET 8、WinUI 3、Windows App SDK 2.3.1、MSIX、现有自定义 console regression harness。

---

## 开工前保护边界

- 工作目录固定为 `E:\project\MFA\mfa-verifier\artifacts\windows`，分支必须是 `codex/windows-mvp`。
- 当前已有未提交改动：`LiubaiMfa.Windows/Package.appxmanifest`、`README.md`、`VERIFICATION.md`、`scripts/new-development-certificate.ps1` 和仓库根目录 `skills-lock.json`。执行本计划前先运行 `git diff -- <file>`，只做逐段合并，不得覆盖、还原或顺手提交这些用户改动。
- `.superpowers/` 是本地忽略目录，不加入 Git。
- 每个任务只提交列出的文件；使用 `git status --short` 校验暂存区。
- 涉及真实 MFA 数据的手工测试只使用虚构 Base32 密钥；测试输出和提交记录不得包含真实密钥、验证码或备份正文。

## Task 1：建立 Windows 平台回归测试与账户查询模型

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj`
- Create: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountQuery.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform/LiubaiMfa.Windows.Platform.csproj`
- Modify: `artifacts/windows/LiubaiMfa.sln`

**Step 1: 先写失败的查询测试**

创建 console regression 项目并引用 `LiubaiMfa.Core`、`LiubaiMfa.Windows.Platform`。`Program.cs` 先注册以下测试：

```csharp
var tests = new (string Name, Action Run)[]
{
    ("搜索覆盖服务、账户和分组", SearchMatchesIssuerAccountAndGroup),
    ("分组过滤与搜索叠加", GroupAndSearchAreCombined),
    ("排序忽略大小写且稳定", SortIsCaseInsensitiveAndStable),
    ("分组计数包含全部和非空分组", GroupCountsAreCorrect),
};

static void SearchMatchesIssuerAccountAndGroup()
{
    var source = Fixtures.Accounts();
    AssertIds(AccountQuery.FilterAndSort(source, "工作", null), "3");
    AssertIds(AccountQuery.FilterAndSort(source, "alice", null), "1");
    AssertIds(AccountQuery.FilterAndSort(source, "google", null), "2");
}
```

固定夹具应包含大小写不同、同 issuer 不同 account、空组和“工作”组，ID 固定为 `1`、`2`、`3`、`4`，不得使用随机值。

regression 项目使用 `net8.0-windows10.0.22621.0`、`RuntimeIdentifier=win-x64`、`TreatWarningsAsErrors=true`。平台项目增加以下 friend assembly，使测试可访问内部纯逻辑，但不扩大正式 API：

```xml
<ItemGroup>
  <InternalsVisibleTo Include="LiubaiMfa.Windows.Platform.Regression" />
</ItemGroup>
```

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 编译失败，明确提示 `AccountQuery` 不存在；不能因为测试未执行而得到假绿灯。

**Step 3: 实现最小纯查询模型**

`AccountQuery.cs` 提供无 WinUI 依赖的 API：

```csharp
internal static class AccountQuery
{
    internal static IReadOnlyList<MfaAccount> FilterAndSort(
        IEnumerable<MfaAccount> source,
        string? searchText,
        string? selectedGroup)
    {
        var query = source.Select((account, index) => (account, index));
        if (!string.IsNullOrWhiteSpace(selectedGroup))
        {
            query = query.Where(x => string.Equals(
                NormalizeGroup(x.account.Group), selectedGroup,
                StringComparison.OrdinalIgnoreCase));
        }

        var term = searchText?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(x => Contains(x.account.Issuer, term)
                || Contains(x.account.Account, term)
                || Contains(NormalizeGroup(x.account.Group), term));
        }

        return query
            .OrderBy(x => x.account.Issuer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.account.Account, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.index)
            .Select(x => x.account)
            .ToArray();
    }

    internal static IReadOnlyList<GroupCount> BuildGroupCounts(IEnumerable<MfaAccount> source)
    {
        var accounts = source.ToArray();
        return new[] { new GroupCount(null, "全部", accounts.Length) }
            .Concat(accounts
                .Select(x => NormalizeGroup(x.Group))
                .Where(x => x.Length > 0)
                .GroupBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new GroupCount(x.Key, x.Key, x.Count())))
            .ToArray();
    }

    private static string NormalizeGroup(string? value) => value?.Trim() ?? string.Empty;
    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
}

internal sealed record GroupCount(string? Value, string Label, int Count);
```

**Step 4: 运行测试并加入解决方案**

```powershell
dotnet sln .\LiubaiMfa.sln add .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 4 项全部显示 `PASS`，进程退出码为 0。

**Step 5: 提交**

```powershell
git add artifacts/windows/LiubaiMfa.sln artifacts/windows/LiubaiMfa.Windows.Platform/LiubaiMfa.Windows.Platform.csproj artifacts/windows/LiubaiMfa.Windows.Platform.Regression artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountQuery.cs
git commit -m "test: cover Windows account filtering"
```

## Task 2：让 MainViewModel 提供可绑定的搜索、分组和派生列表

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/GroupFilterItemViewModel.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/IAccountRepository.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/EncryptedAccountRepository.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/MainViewModel.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 写失败的 ViewModel 状态测试**

在 regression harness 增加固定内存仓库和以下断言：

```csharp
static async Task MainViewModelRefreshesDerivedState()
{
    var repository = new FakeAccountRepository(Fixtures.Accounts());
    var vm = new MainViewModel(repository);
    await vm.LoadAsync();

    vm.SearchText = "alice";
    AssertIds(vm.VisibleAccounts.Select(x => x.Model), "1");

    vm.SearchText = string.Empty;
    vm.SelectedGroup = "工作";
    AssertIds(vm.VisibleAccounts.Select(x => x.Model), "3");
    AssertEqual("全部 (4)", vm.Groups[0].DisplayText);
}
```

再覆盖：删除后计数更新、所选分组被删空后退回“全部”、加载失败时不会把错误当成空账户。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 因 `VisibleAccounts`、`Groups`、`SearchText`、`SelectedGroup` 或接口不存在而失败。

**Step 3: 抽象仓库并实现派生状态**

仓库接口固定为：

```csharp
internal interface IAccountRepository
{
    Task<IReadOnlyList<MfaAccount>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<MfaAccount> accounts, CancellationToken cancellationToken = default);
}
```

`EncryptedAccountRepository` 实现此接口但不改变加密、路径或原子替换行为。`MainViewModel` 实现 `INotifyPropertyChanged`，内部保留 `_accounts` 作为唯一可写源，公开：

```csharp
public ObservableCollection<AccountItemViewModel> VisibleAccounts { get; } = new();
public ObservableCollection<GroupFilterItemViewModel> Groups { get; } = new();
public string SearchText { get; set; }
public string? SelectedGroup { get; set; }
public bool HasAccounts { get; }
public bool HasVisibleAccounts { get; }
public bool HasLoadFailure { get; }
internal IReadOnlyList<MfaAccount> AccountsSnapshot => _accounts.ToArray();
```

所有加载、添加、更新、删除成功路径最终只调用一个 `RefreshDerivedState()`；加载失败设置 `HasLoadFailure=true` 并禁止后续写入，绝不能把 `_accounts` 清空后保存。

**Step 4: 运行回归**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Expected: 两个 regression 项目全部通过。

**Step 5: 提交**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Services/IAccountRepository.cs artifacts/windows/LiubaiMfa.Windows/Services/EncryptedAccountRepository.cs artifacts/windows/LiubaiMfa.Windows/ViewModels artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: add searchable grouped account view model"
```

## Task 3：完善账户行展示模型

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/ServiceIconDescriptor.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/ServiceIconResolver.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 写失败测试**

覆盖以下行为：

```csharp
AssertEqual("123 456", AccountItemViewModel.FormatCode("123456"));
AssertEqual("12 345 678", AccountItemViewModel.FormatCode("12345678"));
AssertEqual(0.5d, AccountItemViewModel.CalculateProgress(15, 30), tolerance: 0.001);
AssertEqual("G", ServiceIconResolver.Resolve("Google").FallbackText);
AssertEqual(ServiceIconKind.FallbackText, ServiceIconResolver.Resolve("未知服务").Kind);
```

再断言 `Refresh(now)` 同时更新 `Code`、`FormattedCode`、`SecondsRemaining`、`ProgressValue`，且属性变化时触发通知。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: 新 API 不存在或断言失败。

**Step 3: 实现展示属性**

`AccountItemViewModel` 保留未分组的 `Code` 用于复制，并新增：

```csharp
public string FormattedCode => FormatCode(Code);
public double ProgressValue { get; private set; }
public string AccessibleName => $"{Issuer}，{Account}，验证码 {FormattedCode}";
public ServiceIconDescriptor Icon => ServiceIconResolver.Resolve(Issuer);

internal static string FormatCode(string value) => value.Length switch
{
    6 => $"{value[..3]} {value[3..]}",
    8 => $"{value[..2]} {value.Substring(2, 3)} {value[5..]}",
    _ => value,
};
```

服务图标第一版只解析内置本地 glyph/品牌色映射；未知服务使用首个文本元素生成确定性色块。不得加入 HTTP、favicon 或遥测请求。

**Step 4: 运行回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
git add artifacts/windows/LiubaiMfa.Windows/ViewModels artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: add compact account row presentation"
```

## Task 4：实现 DPI 安全的小窗口尺寸与位置恢复

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/WindowPlacementService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform/LiubaiMfa.Windows.Platform.csproj`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 先测试纯边界算法**

```csharp
var work = new PixelRect(0, 0, 1920, 1040);
AssertEqual(new PixelRect(1400, 360, 520, 680),
    WindowPlacementService.Clamp(new PixelRect(1800, 900, 520, 680), work, 460, 560));
AssertEqual(new PixelRect(0, 0, 460, 560),
    WindowPlacementService.Clamp(new PixelRect(-200, -300, 200, 300), work, 460, 560));
```

再覆盖负坐标副屏和已移除显示器场景。

**Step 2: 运行并确认失败**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: `WindowPlacementService` 不存在。

**Step 3: 实现窗口策略**

- 默认有效像素 `520 × 680`，最小有效像素 `460 × 560`。
- 用窗口 DPI 把有效像素换算为 `AppWindow.Resize` 所需物理像素。
- 用 `OverlappedPresenter` 设置 `IsMaximizable=false`、保留最小化和调整大小。
- 使用 `ApplicationData.Current.LocalSettings` 保存普通状态下的 `X/Y/Width/Height`；仅在 `AppWindow.Changed` 且不是最小化时保存。
- 恢复前通过 `DisplayArea.GetFromRect(..., DisplayAreaFallback.Primary)` 获取工作区并调用纯 `Clamp`。
- 首次启动居中于主显示器工作区，不自动最大化。

`MainWindow` 构造函数只调用：

```csharp
_placementService.Configure(AppWindow, GetWindowDpi());
_placementService.RestoreOrApplyDefault(AppWindow);
AppWindow.Changed += (_, args) => _placementService.SaveNormalPlacement(AppWindow, args);
```

**Step 4: 运行平台编译检查**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.Platform\LiubaiMfa.Windows.Platform.csproj -c Release
```

Expected: 测试通过、0 warning、0 error。

**Step 5: 提交**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Services/WindowPlacementService.cs artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows.Platform artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: persist compact Windows window placement"
```

## Task 5：建立主题资源、紧凑外壳和锁定视图

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`

**Step 1: 先让代码检查表达新边界**

在 generated stubs 中补齐 `MainWindow` 只应访问的根控件（`ContentHost`、`LockView`）和新视图公开事件所需最小 stub，然后运行：

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
```

Expected: 因真实 `MainWindow.xaml.cs` 仍引用旧控件，构建失败；失败点应限定在待迁移控件名。

**Step 2: 改成主题资源**

`App.xaml` 删除硬编码深色背景，新增只引用平台主题资源的语义刷子：

```xml
<SolidColorBrush x:Key="AppSurfaceBrush"
                 Color="{ThemeResource CardBackgroundFillColorDefault}" />
<SolidColorBrush x:Key="AppTextPrimaryBrush"
                 Color="{ThemeResource TextFillColorPrimary}" />
<SolidColorBrush x:Key="AppTextSecondaryBrush"
                 Color="{ThemeResource TextFillColorSecondary}" />
<Style x:Key="CompactIconButtonStyle" TargetType="Button"
       BasedOn="{StaticResource DefaultButtonStyle}">
    <Setter Property="Width" Value="36" />
    <Setter Property="Height" Value="36" />
    <Setter Property="Padding" Value="0" />
</Style>
```

若 WinUI 2.3 的具体资源名编译失败，只能替换成同版本存在的 `ThemeResource`，不得回退成仅深色可读的固定 RGB。

**Step 3: 创建锁定视图和主视图外壳**

`LockView` 只显示锁图标、“验证码已锁定”、Windows Hello 说明、解锁按钮和 1 分钟自动锁定说明，不绑定账户数量或名称。

`AuthenticatorView` 使用 5 行布局：标题命令区、`AutoSuggestBox`、横向分组、账户列表、底部本地保护/备份入口。顶栏命令固定为“导入二维码”“立即锁定”“添加账户”，每个图标按钮都包含中文 `ToolTipService.ToolTip` 和 `AutomationProperties.Name`。

`MainWindow.xaml` 只保留主题背景和两个视图容器：

```xml
<Grid Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
    <views:AuthenticatorView x:Name="AuthenticatorView" Visibility="Collapsed" />
    <views:LockView x:Name="LockView" />
</Grid>
```

**Step 4: 把生命周期留在 MainWindow**

`MainWindow.xaml.cs` 继续负责 Windows Hello、失活计时、锁定时卸载 ViewModel、两个视图切换；把搜索、列表和账户对话控件访问移到 `AuthenticatorView`。锁定时必须停止验证码 timer 并清空可观察集合。

**Step 5: 编译并提交**

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
git add artifacts/windows/LiubaiMfa.Windows/App.xaml artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows/Views artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs
git commit -m "feat: add compact themed Windows shell"
```

## Task 6：实现 6 行账户列表、复制、左滑和等价上下文命令

**Files:**

- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountActionPolicy.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`

**Step 1: 写交互映射失败测试**

在平台 regression 增加一个纯 `AccountActionPolicy` 测试，断言 Swipe、鼠标右键、`Shift+F10` 都映射到相同的 `Edit/ViewSecret/Delete` 枚举，且 `Delete` 永远需要确认、`ViewSecret` 永远需要 Hello。先引用不存在的类型并运行，确认失败。

**Step 2: 实现纯策略并通过测试**

```csharp
internal enum AccountAction { Edit, ViewSecret, Delete }

internal static class AccountActionPolicy
{
    internal static bool RequiresHello(AccountAction action) =>
        action == AccountAction.ViewSecret;

    internal static bool RequiresConfirmation(AccountAction action) =>
        action == AccountAction.Delete;
}
```

**Step 3: 实现账户 DataTemplate**

- `ListView.ItemContainerStyle` 设置紧凑间距，账户可视内容高度约 60，有 2–3 px 进度线。
- 左侧图标为 36 px；服务名/账户名各一行省略；验证码使用 `FontFamily="Consolas"` 或 WinUI 可用等宽字体并绑定 `FormattedCode`。
- 验证码按钮和复制按钮都调用同一个 `CopyCode` 方法；反馈仅显示“验证码已复制”，不回显数字。
- `SwipeControl.RightItems` 使用 `SwipeMode="Reveal"`，顺序为编辑、查看密钥、删除；不得使用 `Execute`。
- `ContextFlyout` 放相同三项；`Shift+F10` 由 WinUI 上下文菜单默认行为或 `KeyDown` 显式打开。

关键结构：

```xml
<SwipeControl>
  <SwipeControl.RightItems>
    <SwipeItems Mode="Reveal">
      <SwipeItem Text="编辑" Invoked="EditSwipeItem_Invoked" />
      <SwipeItem Text="查看密钥" Invoked="ViewSecretSwipeItem_Invoked" />
      <SwipeItem Text="删除" Invoked="DeleteSwipeItem_Invoked" />
    </SwipeItems>
  </SwipeControl.RightItems>
  <Grid Height="60" AutomationProperties.Name="{Binding AccessibleName}">
    <Grid.RowDefinitions>
      <RowDefinition Height="*" />
      <RowDefinition Height="3" />
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="44" />
      <ColumnDefinition Width="*" />
      <ColumnDefinition Width="Auto" />
      <ColumnDefinition Width="36" />
    </Grid.ColumnDefinitions>
    <Border Width="36" Height="36" CornerRadius="8"
            Background="{Binding Icon.Background}">
      <TextBlock Text="{Binding Icon.FallbackText}"
                 HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Border>
    <StackPanel Grid.Column="1" VerticalAlignment="Center">
      <TextBlock Text="{Binding Issuer}" TextTrimming="CharacterEllipsis" />
      <TextBlock Text="{Binding Account}" TextTrimming="CharacterEllipsis"
                 Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
    </StackPanel>
    <Button Grid.Column="2" Content="{Binding FormattedCode}"
            FontFamily="Consolas" FontSize="24" Tag="{Binding}"
            Click="CopyCodeButton_Click"
            AutomationProperties.Name="复制此账户验证码" />
    <Button Grid.Column="3" Tag="{Binding}" Click="CopyCodeButton_Click"
            Style="{StaticResource CompactIconButtonStyle}"
            ToolTipService.ToolTip="复制验证码"
            AutomationProperties.Name="复制验证码">
      <FontIcon Glyph="&#xE8C8;" />
    </Button>
    <ProgressBar Grid.Row="1" Grid.ColumnSpan="4" Minimum="0" Maximum="1"
                 Value="{Binding ProgressValue}" IsHitTestVisible="False"
                 AutomationProperties.Name="验证码剩余时间" />
  </Grid>
</SwipeControl>
```

三个入口最终都调用 `RequestAccountAction(AccountItemViewModel, AccountAction)`，不能复制安全判断。

**Step 4: 键盘复制**

当账户行处于焦点且收到 `Ctrl+C` 时复制当前行 `Code`。剪贴板写入失败显示克制错误；锁定状态、无选中行或验证码为空时不执行。

**Step 5: 编译并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
git add artifacts/windows/LiubaiMfa.Windows/Views artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountActionPolicy.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: add swipe account actions"
```

## Task 7：拆分账户对话、状态反馈和新账户定位

**Files:**

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/AccountDialogService.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/MainViewModel.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

**Step 1: 写失败的新增后可见性测试**

```csharp
static async Task AddedAccountBecomesVisible()
{
    var vm = new MainViewModel(new FakeAccountRepository(Fixtures.Accounts()));
    await vm.LoadAsync();
    vm.SearchText = "不会匹配";
    vm.SelectedGroup = "工作";

    var added = await vm.AddAsync(Fixtures.NewPersonalAccount());

    AssertEqual(string.Empty, vm.SearchText);
    AssertEqual(null, vm.SelectedGroup);
    AssertEqual(added.Id, vm.PendingRevealAccountId);
}
```

另测：保存失败时筛选、列表和 `PendingRevealAccountId` 都保持原值。

**Step 2: 运行并确认失败，然后实现状态契约**

成功添加/导入后，ViewModel 清除会隐藏新账户的筛选，设置一次性 `PendingRevealAccountId`。视图刷新后调用 `ListView.ScrollIntoView`、聚焦该行，并用不依赖颜色的短暂边框/图标高亮；随后调用 `ConsumePendingReveal()` 清除 ID。

**Step 3: 拆分对话服务**

`AccountDialogService` 提供：

```csharp
Task<AccountDraft?> ShowAddAsync(XamlRoot root);
Task<AccountEdit?> ShowEditAsync(XamlRoot root, MfaAccount account);
Task<bool> ConfirmDeleteAsync(XamlRoot root, MfaAccount account);
Task ShowSecretAsync(XamlRoot root, MfaAccount account);
```

删除正文必须包含 issuer/account；查看密钥在服务内部先请求 `WindowsHelloService`，取消时只返回取消状态。编辑不展示也不修改密钥。

`BackupDialogService` 承接现有导入/导出 UI，但必须继续复用现有 `BackupCrypto` 和仓库，不改变 v2 外层格式。恢复密码仅在内存中存活，不写日志和本地设置。

**Step 4: 完成空状态、无结果和 InfoBar**

- 空账户：显示“尚未添加账户”，提供“添加账户”“导入二维码”。
- 有账户但无结果：显示“没有匹配的账户”和“清除搜索和筛选”。
- 加载失败：保持锁定并禁写，不显示为空账户。
- 成功反馈使用自动关闭 `InfoBar`；错误只显示恢复建议，不包含密钥、验证码或文件正文。

**Step 5: 回归并提交**

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
git add artifacts/windows/LiubaiMfa.Windows/Services/AccountDialogService.cs artifacts/windows/LiubaiMfa.Windows/Services/BackupDialogService.cs artifacts/windows/LiubaiMfa.Windows/Views artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows/ViewModels/MainViewModel.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "refactor: separate compact account workflows"
```

## Task 8：全量自动验证、真实窗口验收和文档合并

**Files:**

- Modify carefully: `artifacts/windows/README.md`
- Modify carefully: `artifacts/windows/VERIFICATION.md`
- Modify as needed: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`

**Step 1: 全量自动验证**

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build .\LiubaiMfa.Windows.Platform\LiubaiMfa.Windows.Platform.csproj -c Release
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet restore .\LiubaiMfa.sln
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

Expected: 所有命令退出码 0；Release 解决方案 0 warning、0 error。

**Step 2: 构建并启动真实 MSIX**

先查看当前脚本差异，保留用户修改：

```powershell
git diff -- .\scripts\new-development-certificate.ps1 .\LiubaiMfa.Windows\Package.appxmanifest
```

使用仓库现有证书/MSIX 脚本构建并安装；若需要改脚本或 manifest，只允许合并必要变更，不得还原现有内容。启动标题为“留白 MFA”的已安装应用并保持运行完成以下检查。

**Step 3: 真实界面验收清单**

- 首次普通窗口约 `520 × 680` 有效像素，未最大化，最大化按钮不可用。
- 默认高度能完整显示 6 个账户行；拖高后显示更多，拖宽后仍为单列。
- 搜索 issuer/account/group 即时生效；组计数随增删更新。
- 单击验证码、复制图标和账户焦点下 `Ctrl+C` 均复制；提示不显示验证码。
- 左滑只 Reveal“编辑/查看密钥/删除”；右键、`Shift+F10` 有同样命令。
- 查看密钥再次触发 Windows Hello；删除显示账户标识并二次确认。
- 失活超过 1 分钟后锁定；锁定页不泄露账户元数据。
- Light、Dark、High Contrast 三种模式文字和焦点环可读。
- 关闭并重开后恢复普通尺寸/位置；拔掉或禁用副屏后窗口仍回到可见工作区。

**Step 4: 合并文档**

在 `README.md` 增加紧凑窗口和交互说明；在 `VERIFICATION.md` 追加本次自动命令、实际输出摘要、MSIX 启动时间和手工检查结果。不能覆盖两个文件中现有未提交段落。

**Step 5: 最终差异审计**

```powershell
git diff --check
git status --short
git diff --stat
git diff -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest artifacts/windows/README.md artifacts/windows/VERIFICATION.md artifacts/windows/scripts/new-development-certificate.ps1
```

确认没有 Android/HarmonyOS 文件、真实 MFA 数据、调试日志或意外二进制进入差异。

**Step 6: 提交本任务文档变更**

仅暂存本计划实际新增的 README/VERIFICATION 行；若 manifest/证书脚本的修改属于用户原改动或下一计划，不纳入本提交。

```powershell
git add -p artifacts/windows/README.md artifacts/windows/VERIFICATION.md
git commit -m "docs: verify compact Windows interface"
```

## 完成门槛

以下条件全部满足才算完成：自动测试和 Release 构建全绿；真实 MSIX 小窗口已启动并完成 Light/Dark/High Contrast 与鼠标/键盘/左滑验证；6 行账户在默认窗口完整可见；锁定和敏感操作安全边界未退化；现有未提交用户改动已保留；没有修改其他平台代码。
