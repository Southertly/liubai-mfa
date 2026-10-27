# Windows MFA 添加账户入口与截图扫描 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将所有新增账户方式统一到同窗口“添加账户”页面，并通过 Windows 11 Snipping Tool 矩形截图回调安全导入屏幕上的 TOTP 二维码。

**Architecture:** 先更新独立浏览器评审镜像，再在 WinUI 中增加 `AddAccountView`。截图协议由纯状态策略、Windows 协议服务和单实例激活路由分层处理；截图与图片文件最终复用同一二维码解析、确认和保存管线。截图文件只读取一次，不写入 LocalState 或剪贴板。

**Tech Stack:** WinUI 3、C#、.NET 8、Windows App SDK 2.3.1、MSIX、Snipping Tool `ms-screenclip` 协议、ZXing.Net、HTML/CSS/原生 JavaScript

---

## 文件结构

### 浏览器评审镜像

- Modify: `C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`
  - 隐藏标题栏名称，移除独立图片导入，增加“添加账户”完整页面和可返回交互。

### WinUI 视图

- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml`
  - 展示返回、截图扫描、图片导入和手动输入。
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml.cs`
  - 只发出四种用户意图，不访问文件或账户。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
  - 移除独立二维码按钮，统一“＋”和空状态入口。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
  - 删除 `ImportQrRequested`，保留单一 `AddRequested`。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml`
  - 增加空白自定义标题栏拖动区和 `AddAccountView`。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
  - 协调三种页面状态、截图请求、会话锁定和统一导入结果。

### 截图与激活

- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingCaptureState.cs`
  - 纯状态机，处理关联 ID、状态码、锁定延期和一次性令牌。
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolProtocol.cs`
  - 纯 URI 构建和回调解析，不启动系统 UI。
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolCaptureService.cs`
  - 使用 `Launcher.LaunchUriAsync` 和 `SharedStorageAccessManager`。
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/QrAccountImportCoordinator.cs`
  - 统一截图文件与图片文件的解析、确认和保存管线。
- Create: `artifacts/windows/LiubaiMfa.Windows/Program.cs`
  - 在 WinUI 初始化前注册固定实例键，将协议激活重定向到现有进程。
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml.cs`
  - 接收首次及重定向激活，并在 UI 线程交给 `MainWindow`。
- Modify: `artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj`
  - 禁用 XAML 自动生成入口，以启用自定义 `Program.Main`。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`
  - 只增加 `liubai-mfa` 协议扩展；保留用户已有版本号和 `runFullTrust` 差异。

### 测试与编译桩

- Modify: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`
  - 增加截图状态、协议解析和统一导入策略回归。
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj`
  - 编译新增视图代码后置文件。
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`
  - 增加 `AddAccountView` 和标题栏字段桩。

## Task 1: 更新浏览器评审镜像

**Files:**
- Modify: `C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`

- [ ] **Step 1: 运行新交互契约并确认 RED**

```powershell
$page = 'C:\Users\liu\.codex\visualizations\2026\08\19\01a01992-9348-7740-b771-ef34ffea2d43\mfa-ui-brainstorm\content\visual-direction.html'
$html = Get-Content -LiteralPath $page -Raw
@('id="add-account-view"', 'data-action="open-add"', '截图并扫描二维码', '从图片导入', '手动输入') | ForEach-Object {
    if (-not $html.Contains($_)) { throw "缺少添加账户镜像契约：$_" }
}
if ($html.Contains('aria-label="从图片导入（演示）"')) { throw '首页仍存在独立图片导入按钮' }
```

Expected: FAIL，首个缺失项为 `id="add-account-view"`。

- [ ] **Step 2: 写入双视图结构**

保留现有虚构账户首页，将标题栏文字节点替换为空白拖动区；页头操作只保留设置和 `data-action="open-add"` 的“＋”。新增：

```html
<section id="add-account-view" class="app-view add-account-view" hidden>
  <header class="add-header">
    <button type="button" data-action="back-to-list" aria-label="返回验证码列表">返回</button>
    <div><h2>添加账户</h2><p>选择一种添加方式</p></div>
  </header>
  <button type="button" class="capture-hero" data-action="preview-capture">
    <span class="capture-illustration" aria-hidden="true"></span>
    <strong>截图并扫描二维码</strong>
    <span>框选屏幕上的二维码，识别后可确认账户信息</span>
  </button>
  <div class="other-methods">
    <h3>其他方式</h3>
    <button type="button"><strong>从图片导入</strong><span>选择 PNG、JPG 或 BMP 图片</span></button>
    <button type="button"><strong>手动输入</strong><span>输入服务、账户和密钥</span></button>
  </div>
</section>
```

- [ ] **Step 3: 实现可中断的页面切换**

```javascript
const listView = root.querySelector("#account-list-view");
const addView = root.querySelector("#add-account-view");
const showAddView = () => {
  listView.hidden = true;
  addView.hidden = false;
  addView.querySelector("[data-action='back-to-list']").focus();
};
const showListView = () => {
  addView.hidden = true;
  listView.hidden = false;
  root.querySelector("[data-action='open-add']").focus();
};
root.querySelector("[data-action='open-add']").addEventListener("click", showAddView);
root.querySelector("[data-action='back-to-list']").addEventListener("click", showListView);
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !addView.hidden) showListView();
});
```

使用 180ms `opacity` 与 `transform` 过渡；按键进入时跳过位移；`prefers-reduced-motion` 下仅切换可见性和焦点。

- [ ] **Step 4: 运行契约并确认 GREEN**

重新运行 Step 1，并额外断言首页标题栏不含 `<span>留白 MFA</span>`。Expected: PASS。

- [ ] **Step 5: 在内置浏览器验收**

打开 `http://127.0.0.1:57111/visual-direction.html`，点击“＋”，确认完整添加页出现；点击返回和按 `Esc` 均回到账户列表。浏览器镜像只使用虚构数据，不触发文件选择或系统截图。

## Task 2: 新增添加账户视图并统一入口

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml`
- Create: `artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 增加源契约 RED 测试**

在 Platform Regression 增加 `VerifyUnifiedAddAccountEntrySourceContract`：读取 `AuthenticatorView.xaml`、`AuthenticatorView.xaml.cs` 和 `AddAccountView.xaml`，断言首页不存在 `ImportQrButton_Click`/`ImportQrRequested`，存在单一 `AddRequested`，并断言添加页包含 `ScreenshotScanRequested`、`ImportImageRequested`、`ManualEntryRequested`、`BackRequested` 和对应简体中文文案。

Run:

```powershell
dotnet run --project artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

Expected: FAIL，`AddAccountView.xaml` 不存在。

- [ ] **Step 2: 新建意图型用户控件**

`AddAccountView.xaml.cs` 使用以下公开边界：

```csharp
public sealed partial class AddAccountView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? ScreenshotScanRequested;
    public event EventHandler? ImportImageRequested;
    public event EventHandler? ManualEntryRequested;

    public AddAccountView() => InitializeComponent();

    public void FocusPrimaryAction() => ScreenshotScanButton.Focus(FocusState.Programmatic);
}
```

XAML 使用顶部返回按钮、一个 Accent 主卡片和两个次级列表按钮；每个按钮高度至少 44px，包含工具提示、自动化名称和一致的 `FontIcon`。

- [ ] **Step 3: 统一首页入口**

删除 `AuthenticatorView` 顶部及空状态的独立二维码按钮和对应点击处理；顶部与空状态“添加账户”都继续触发 `AddRequested`。保留设置和条件锁定按钮。

- [ ] **Step 4: 更新编译桩并验证 GREEN**

将 `AddAccountView.xaml.cs` 加入 CodeBehindCheck 项目，并在 `GeneratedXamlStubs.cs` 增加 `AddAccountView` 的 `ScreenshotScanButton` 字段。运行 Platform Regression 与 CodeBehindCheck，Expected: 全部 PASS、0 warning、0 error。

- [ ] **Step 5: 提交视图入口**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml artifacts/windows/LiubaiMfa.Windows/Views/AddAccountView.xaml.cs artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs
git commit -m "feat: unify account add entry"
```

## Task 3: 截图协议纯策略与状态机

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingCaptureState.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolProtocol.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 写状态机 RED 测试**

增加以下行为用例：

```csharp
var state = new SnippingCaptureState();
var first = state.Begin(Guid.Parse("11111111-1111-1111-1111-111111111111"));
var deferred = state.Receive(first, 200, "token-a", sessionCurrent: false);
Assert(deferred.Kind == SnippingCaptureDecisionKind.DeferUntilUnlock, "锁定时必须延期处理。");
var resumed = state.ResumeAfterUnlock(sessionCurrent: true);
Assert(resumed.Kind == SnippingCaptureDecisionKind.RedeemNow && resumed.FileAccessToken == "token-a", "解锁后必须只返回一次令牌。");
Assert(state.ResumeAfterUnlock(true).Kind == SnippingCaptureDecisionKind.Ignore, "一次性令牌不得重复返回。");
```

另测关联 ID 不匹配、`499` 安静取消、`400`、`408`、`500`、`200` 缺令牌、新请求替换旧请求和 `Abandon()`。

Run Platform Regression. Expected: FAIL，类型不存在。

- [ ] **Step 2: 实现最小状态边界**

```csharp
internal enum SnippingCaptureDecisionKind
{
    Ignore,
    RedeemNow,
    DeferUntilUnlock,
    Cancelled,
    BadRequest,
    Timeout,
    Failed,
}

internal readonly record struct SnippingCaptureDecision(
    SnippingCaptureDecisionKind Kind,
    string? FileAccessToken = null);
```

`SnippingCaptureState` 同时只保存一个关联 ID 和一个待处理令牌；所有终态必须清空当前请求，延期态只在 `ResumeAfterUnlock(true)` 时消费令牌。

- [ ] **Step 3: 写协议 RED 测试**

断言 `SnippingToolProtocol.CreateCaptureUri(id)` 精确包含裸参数 `rectangle`、`enabledModes=RectangleSnip`、`user-agent=LiubaiMFA`、`api-version=1.2`、`redirect-uri=liubai-mfa%3A%2F%2Fcapture-response` 和 `x-request-correlation-id`。断言 `TryParseResponse` 只接受 `liubai-mfa://capture-response`，拒绝缺少状态码、无效 GUID、其他 host 和重复关键参数。

- [ ] **Step 4: 实现 URI 构建与解析并验证 GREEN**

`SnippingToolProtocol` 使用 `UriBuilder`/安全查询编码生成请求；解析返回：

```csharp
internal readonly record struct SnippingToolResponse(
    Guid CorrelationId,
    int StatusCode,
    string? FileAccessToken);
```

运行 Platform Regression，Expected: 新旧用例全部 PASS。

- [ ] **Step 5: 提交协议策略**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Services/SnippingCaptureState.cs artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolProtocol.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: model screenshot capture responses"
```

## Task 4: 单实例与协议激活路由

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Program.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 写入口与激活源契约 RED 测试**

断言项目文件包含 `DISABLE_XAML_GENERATED_MAIN`，`Program.cs` 使用固定实例键 `liubai-mfa-main`、`FindOrRegisterForKey` 和 `RedirectActivationToAsync`，`App.xaml.cs` 同时消费首次激活和重定向激活，并通过 UI Dispatcher 调用主窗口。

Run Platform Regression. Expected: FAIL，`Program.cs` 不存在。

- [ ] **Step 2: 实现自定义单实例入口**

`Program.Main` 在任何 XAML/WinRT UI 初始化前执行以下固定顺序：初始化 COM 包装器、取得首次 `AppActivationArguments`、注册固定实例键；若不是当前实例，同步等待 `RedirectActivationToAsync` 完成后立即退出；若是当前实例，先注册重定向事件，再启动 `Application`。不要创建第二个窗口。

```csharp
[STAThread]
public static async Task Main(string[] args)
{
    ComWrappersSupport.InitializeComWrappers();
    var current = AppInstance.GetCurrent();
    var initial = current.GetActivatedEventArgs();
    var main = AppInstance.FindOrRegisterForKey("liubai-mfa-main");
    if (!main.IsCurrent)
    {
        await main.RedirectActivationToAsync(initial);
        return;
    }

    App.SetInitialActivation(initial);
    main.Activated += App.OnAppInstanceActivated;
    Application.Start(_ =>
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherQueueSynchronizationContext(dispatcher));
        _ = new App();
    });
}
```

`LiubaiMfa.Windows.csproj` 增加：

```xml
<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
```

- [ ] **Step 3: 将激活派发到 UI 线程**

`App` 使用静态锁保护“首次激活参数 + 尚未创建 UI 时到达的重定向参数 FIFO 队列”。`SetInitialActivation` 只允许 `Program.Main` 在 `Application.Start` 前调用；`OnAppInstanceActivated` 在窗口未就绪时入队，窗口就绪后通过 `DispatcherQueue.TryEnqueue` 调用实例方法。实例方法仅在 `ExtendedActivationKind.Protocol` 且数据为 `IProtocolActivatedEventArgs` 时调用 `MainWindow.HandleProtocolActivationAsync(uri)`；普通 Launch 只激活现有窗口。窗口创建后必须按“首次参数 → 启动间隙内重定向参数”的顺序逐个消费并清空引用，不能覆盖或重复处理回调。

- [ ] **Step 4: 验证编译与回归**

运行 Platform Regression、CodeBehindCheck 和 WinUI x64 build。Expected: 全部 PASS，0 warning、0 error。

- [ ] **Step 5: 提交单实例激活**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Program.cs artifacts/windows/LiubaiMfa.Windows/App.xaml.cs artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs
git commit -m "feat: route protocol activation to one instance"
```

## Task 5: 截图服务与统一二维码导入

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolCaptureService.cs`
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/QrAccountImportCoordinator.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 写协调器 RED 测试**

使用真实委托而非 mock 框架覆盖：有效 `otpauth://` 进入确认并保存一次；无二维码返回 `InvalidQr`；确认取消不保存；会话失效不保存；保存重复返回 `Conflict`。结果类型：

```csharp
internal enum QrAccountImportOutcome
{
    Imported,
    Cancelled,
    InvalidQr,
    Conflict,
}
```

Run Platform Regression. Expected: FAIL，协调器不存在。

- [ ] **Step 2: 实现统一导入协调器**

协调器采用以下完整边界，使 Platform Regression 可以传入真实委托测试，不依赖 UI mock 框架：

```csharp
internal sealed class QrAccountImportCoordinator
{
    private readonly Func<StorageFile, Task<string?>> _decodeAsync;
    private readonly Func<AccountDraft, Task<AccountDraft?>> _confirmAsync;
    private readonly Func<AccountDraft, Task> _saveAsync;
    private readonly Func<bool> _isSessionCurrent;

    public QrAccountImportCoordinator(
        Func<StorageFile, Task<string?>> decodeAsync,
        Func<AccountDraft, Task<AccountDraft?>> confirmAsync,
        Func<AccountDraft, Task> saveAsync,
        Func<bool> isSessionCurrent);

    public Task<QrAccountImportOutcome> ImportAsync(StorageFile file);
}
```

`ImportAsync` 严格按“解码 → `OtpAuthUriParser.TryParse` → 会话检查 → 构造 `AccountDraft` → 确认 → 会话检查 → 保存”执行。解码或解析失败返回 `InvalidQr`；用户取消或任一会话检查失败返回 `Cancelled`；保存抛出 `InvalidOperationException` 时返回 `Conflict`；其他 I/O、格式和取消异常继续向调用方传播。截图与文件选择都调用同一个协调器实例，不得复制导入代码路径。

- [ ] **Step 3: 实现 Windows 截图服务**

```csharp
internal sealed class SnippingToolCaptureService
{
    public Task<bool> LaunchAsync(Guid correlationId) =>
        Launcher.LaunchUriAsync(SnippingToolProtocol.CreateCaptureUri(correlationId)).AsTask();

    public Task<StorageFile> RedeemAsync(string token) =>
        SharedStorageAccessManager.RedeemTokenForFileAsync(token).AsTask();
}
```

不传 `auto-save`，不调用剪贴板，不复制截图到应用目录。

- [ ] **Step 4: 验证协调器 GREEN**

运行 Platform Regression。Expected: 新旧用例全部 PASS。

- [ ] **Step 5: 提交服务与协调器**

```powershell
git add artifacts/windows/LiubaiMfa.Windows/Services/SnippingToolCaptureService.cs artifacts/windows/LiubaiMfa.Windows/Services/QrAccountImportCoordinator.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: share QR account import pipeline"
```

## Task 6: 主窗口导航、标题栏与截图生命周期

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs`

- [ ] **Step 1: 写主窗口源契约 RED 测试**

断言：`ExtendsContentIntoTitleBar = true`、调用 `SetTitleBar(TitleBarDragRegion)`、不把应用名称放进标题栏、`AddAccountView` 四个事件均已订阅、`HandleProtocolActivationAsync` 使用 `SnippingCaptureState`，`EnterMainViewAsync` 在解锁后消费延期截图，`ForceLock` 不丢弃已验证延期回调。

- [ ] **Step 2: 实现三视图导航**

在 `MainWindow.xaml` 使用 32px 顶部拖动行，下方叠放 `AuthenticatorView`、`AddAccountView`、`LockView`。`ShowAddAccountView()` 只在当前解锁会话有效时切换；`ShowAuthenticatorView()` 恢复列表。锁定时隐藏两个解锁视图。

- [ ] **Step 3: 接入三种添加方式**

- `ManualEntryRequested`：调用现有 `ShowAddAsync`。
- `ImportImageRequested`：调用现有 `OpenQrImageAsync`，随后交给统一协调器。
- `ScreenshotScanRequested`：`Begin(Guid.NewGuid())` 后调用截图服务；启动失败时清除状态并显示恢复提示。

- [ ] **Step 4: 处理协议回调与自动锁定**

`HandleProtocolActivationAsync` 解析响应并取得状态决策：取消无提示；错误显示规格中的简体中文；`RedeemNow` 兑换文件并导入；`DeferUntilUnlock` 只保留内存状态并显示锁定页。`EnterMainViewAsync` 建立新会话并显示主视图后调用 `ResumeAfterUnlock(true)`，有令牌时继续导入。

- [ ] **Step 5: 实现空白自定义标题栏**

保留 `<Window Title="留白 MFA">` 供任务栏和辅助功能使用，但启用扩展标题栏并将空白 `TitleBarDragRegion` 设为拖动区；不渲染应用图标或名称。系统标题按钮背景保持透明。

- [ ] **Step 6: 验证主窗口 GREEN 并提交**

运行 Platform Regression、CodeBehindCheck、WinUI x64 build。Expected: 全部 PASS，0 warning、0 error。

```powershell
git add artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/GeneratedXamlStubs.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression/Program.cs
git commit -m "feat: add screenshot account flow"
```

## Task 7: 安全添加 MSIX 协议扩展

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`

- [ ] **Step 1: 记录并核对用户已有清单差异**

```powershell
git diff -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest
```

Expected: 仍显示用户的 `Version 1.0.0.2` 和 `runFullTrust` 两处差异。不得重写或还原。

- [ ] **Step 2: 增加协议扩展**

只在现有 `<Application>` 下增加：

```xml
<Extensions>
  <uap:Extension Category="windows.protocol">
    <uap:Protocol Name="liubai-mfa" DesiredView="default">
      <uap:DisplayName>留白 MFA 截图回调</uap:DisplayName>
    </uap:Protocol>
  </uap:Extension>
</Extensions>
```

- [ ] **Step 3: 验证并只暂存协议 hunk**

构建 WinUI x64 和 MSIX。运行 `git add -p -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest`：版本号 hunk 选择 `n`，协议 `<Extensions>` hunk 选择 `y`，`runFullTrust` hunk 选择 `n`；若 Git 将相邻内容合并，先选择 `s` 拆分，再按相同规则处理。随后用下列检查确认暂存区不包含版本号和 `runFullTrust`，工作区仍包含这两处用户差异：

```powershell
git diff --cached -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest
git diff -- artifacts/windows/LiubaiMfa.Windows/Package.appxmanifest
```

Expected: cached 只含协议扩展；working tree 仍保留用户原有两处未提交差异。

- [ ] **Step 4: 提交协议注册**

```powershell
git commit -m "feat: register screenshot callback protocol"
```

## Task 8: 完整回归与真实 MSIX 验证

**Files:**
- Verify: `artifacts/windows/*`
- Verify: `C:/Users/liu/.codex/visualizations/2026/08/19/01a01992-9348-7740-b771-ef34ffea2d43/mfa-ui-brainstorm/content/visual-direction.html`

- [ ] **Step 1: 运行完整自动回归**

```powershell
dotnet run --project artifacts/windows/LiubaiMfa.Core.Regression/LiubaiMfa.Core.Regression.csproj -c Release
dotnet run --project artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj -c Release
dotnet build artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
dotnet build artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
dotnet build artifacts/windows/LiubaiMfa.sln -c Release -p:Platform=x64
git diff --check
```

Expected: Core、Platform 全部 PASS；三个 build 均 0 warning、0 error；diff check 无输出。

- [ ] **Step 2: 构建并签名递增版本 MSIX**

使用现有 `scripts/build-msix.ps1` 从当前 HEAD 与用户清单构建新版本，保持同包族原位升级。构建前后记录正式 `accounts.v2.json` 与 `device-key.v1.bin` 的 SHA-256，不输出文件内容。

- [ ] **Step 3: 验证截图协议冷启动与热激活**

分别在应用关闭和已运行时执行截图：只出现一个主窗口；矩形截图有效二维码时进入确认；`Esc` 返回无警告；无二维码显示重新截图路径。

- [ ] **Step 4: 验证自动锁定跨截图**

开启启动保护，开始截图后等待超过自动锁定时限，再完成截图。Expected: 先显示 Windows Hello 解锁，解锁成功后才兑换令牌和显示账户确认。

- [ ] **Step 5: 验证隐私与数据完整性**

确认应用 LocalState 没有新增截图文件、剪贴板未被截图流程写入、正式数据哈希升级前后一致，且新安装以来无新增 Application Error/WER。

- [ ] **Step 6: 保持浏览器镜像和正式窗口供用户验收**

内置浏览器保持在添加账户页；正式应用保持一个响应中的窗口。向用户说明浏览器数据为虚构，正式截图功能处理真实选区但不保存截图。
