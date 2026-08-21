# MFA Windows UI 重设计实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 Windows MFA 客户端上落地已确认的 Fluent 2 单列 UI、账户点击复制/左滑操作和 Windows Hello/电脑密码双解锁体验。

**Architecture:** 保留现有 `AuthenticatorView`、`AddAccountView`、`LockView` 和 `MainWindow` 事件边界；把视觉调整集中到应用资源和 XAML，把复制反馈与解锁方式建模为可测试的状态。账户行继续使用 WinUI `SwipeControl` 的右侧揭示动作（对应向左拖动），移除右键菜单和独立复制按钮。电脑密码使用 Windows 系统凭据对话框验证，不在应用内保存密码。

**Tech Stack:** WinUI 3、C#、.NET 8、XAML ThemeResource、Windows.Security.Credentials.UI、Win32 Credential UI（仅用于系统凭据验证）、现有 Core/Platform 回归项目。

---

## 文件地图

- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml` — Fluent 主题资源、账户卡片和顶部按钮尺寸。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml` — 首页布局、设置菜单、账户行视觉和左滑动作。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs` — 账户行点击复制、1 秒反馈、清理右键菜单及事件状态。
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs` — `IsCopied`/`CodeDisplay` 等绑定状态与可访问名称。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml` — Hello/电脑密码双路径锁定页。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml.cs` — 两个解锁请求事件及状态切换。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/ProtectionSettingsDialogService.cs` — 启动保护设置文案与解锁方式说明。
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/WindowsCredentialService.cs` — Windows 系统凭据验证适配器；密码只在本次验证调用期间存在并清零。
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/WindowsHelloService.cs` — 保持 Hello 可用性查询，统一返回可用性状态。
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs` — 根据 Hello 可用性选择解锁入口，接入电脑密码回退。
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/Program.cs` — 静态检查新增的 XAML 事件、无 ContextFlyout 和账户复制结构。
- Modify/Create: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/*` — 为电脑密码服务和保护策略增加可运行回归测试（不打印凭据）。

### Task 1: 先锁定当前基线并建立可测试状态

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/*`

- [ ] **Step 1: 为复制反馈增加红测试**

  增加纯状态测试，验证复制成功状态的显示值为“已复制”，过期后恢复 `FormattedCode`；失败状态不得改变验证码文本。

- [ ] **Step 2: 运行回归测试确认当前实现不满足**

  Run:

  ```powershell
  dotnet test artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj --configuration Release
  ```

  Expected: 新增的复制反馈测试在实现前失败，现有回归测试保持可区分的通过/失败结果。

- [ ] **Step 3: 实现最小复制状态模型**

  在 `AccountItemViewModel` 中增加 `IsCopied`、`CodeDisplay` 和 `AccessibleCodeDisplay`；计时器只存在于 View 层，ViewModel 不持有 UI 定时器。代码刷新时强制清除复制状态并触发属性通知。

- [ ] **Step 4: 运行测试确认状态通过并提交**

  ```powershell
  dotnet test artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj --configuration Release
  git add artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression
  git commit -m "feat: model one-second code copy feedback"
  ```

### Task 2: 重做首页视觉层和顶部操作

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/App.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`

- [ ] **Step 1: 调整资源和账户卡片层级**

  将账户卡片统一为 ThemeResource 背景、边框、焦点和悬停状态；保留 44px 触控目标、8px 网格、等宽验证码字体和高对比度可读性。

- [ ] **Step 2: 收敛顶部操作为设置与加号**

  删除独立的锁定按钮；设置按钮改为打开已有 Flyout，菜单顺序为：立即锁定、启动保护、分隔线、导入备份、导出加密备份。底部备份按钮只保留本机保护与刷新状态文本。

- [ ] **Step 3: 保持 `+` 页面入口不变并优化空状态**

  `AddButton_Click` 继续触发 `AddRequested`；空状态主按钮也复用该事件，确保截图扫描、图片导入、手动输入仍只在 `AddAccountView` 出现。

- [ ] **Step 4: 构建 XAML 并提交**

  ```powershell
  dotnet build artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj --configuration Release --no-restore
  git add artifacts/windows/LiubaiMfa.Windows/App.xaml artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs
  git commit -m "feat: refine authenticator shell and settings menu"
  ```

### Task 3: 落地账户点击复制和左滑操作

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs`

- [ ] **Step 1: 删除独立复制按钮与 `ContextFlyout`**

  账户卡片只保留一个可点击主体；移除复制图标按钮、账户行 `ContextFlyout` 和三个右键处理方法。保留 `SwipeControl.RightItems`，顺序固定为编辑、查看密钥、删除。

- [ ] **Step 2: 把账户主体点击接到已有 `CopyRequested`**

  给账户主体增加 `Tapped`/键盘 Enter 等价入口，统一调用一个 `RequestCopy(accountId)` 方法。不要新增第二套复制服务或自动重试。

- [ ] **Step 3: 添加 1 秒局部反馈**

  复制成功时设置 `IsCopied=true` 并启动可取消的 1 秒 DispatcherTimer；验证码位置绑定 `CodeDisplay`，显示“已复制”后恢复原验证码。失败时走现有 `ShowStatus("复制失败", Warning)`，不得显示密钥或验证码内容。

- [ ] **Step 4: 运行回归与静态检查并提交**

  ```powershell
  dotnet test artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj --configuration Release
  dotnet build artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/LiubaiMfa.Windows.CodeBehindCheck.csproj --configuration Release --no-restore
  git add artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml artifacts/windows/LiubaiMfa.Windows/Views/AuthenticatorView.xaml.cs artifacts/windows/LiubaiMfa.Windows/ViewModels/AccountItemViewModel.cs
  git commit -m "feat: copy codes from account rows and reveal swipe actions"
  ```

### Task 4: 增加 Windows 电脑密码解锁回退

**Files:**
- Create: `artifacts/windows/LiubaiMfa.Windows/Services/WindowsCredentialService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Services/WindowsHelloService.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml`
- Modify: `artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml.cs`
- Modify: `artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs`
- Test: `artifacts/windows/LiubaiMfa.Windows.Platform.Regression/*`

- [ ] **Step 1: 先写凭据服务契约测试**

  覆盖三种结果：用户取消返回 false、系统凭据验证成功返回 true、验证失败返回 false；测试替身只断言调用参数和结果，不写入或打印密码。

- [ ] **Step 2: 用系统凭据 UI 实现一次性验证**

  使用 Windows Credential UI 收集登录凭据，随后交给系统认证调用；设置不保存凭据的标志，调用结束后清零任何暂存的密码缓冲区。禁止把密码放入 `ApplicationData`、日志、异常文本或 ViewModel。

- [ ] **Step 3: 扩展锁定页为双路径**

  支持 Hello 时显示“使用 Windows Hello 解锁”和次级“使用电脑密码解锁”；Hello 不可用时只显示电脑密码主按钮。错误提示使用“验证失败，请重试”，不泄露系统账户细节。

- [ ] **Step 4: 更新启动保护设置文案和主窗口分流**

  设置页说明改为“优先 Windows Hello，不可用时使用电脑密码”；手动锁定和启动保护共用锁定页。根据 `WindowsHelloService.IsAvailableAsync()` 决定按钮默认路径，但始终保留密码回退。

- [ ] **Step 5: 运行平台回归并提交**

  ```powershell
  dotnet test artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj --configuration Release
  dotnet build artifacts/windows/LiubaiMfa.Windows/LiubaiMfa.Windows.csproj --configuration Release --no-restore
  git add artifacts/windows/LiubaiMfa.Windows/Services artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml artifacts/windows/LiubaiMfa.Windows/Views/LockView.xaml.cs artifacts/windows/LiubaiMfa.Windows/MainWindow.xaml.cs artifacts/windows/LiubaiMfa.Windows.Platform.Regression
  git commit -m "feat: add Windows password unlock fallback"
  ```

### Task 5: 视觉验收和发布前验证

**Files:**
- Modify: `artifacts/windows/LiubaiMfa.Windows.CodeBehindCheck/Program.cs`
- Reference: `artifacts/windows/VERIFICATION.md`

- [ ] **Step 1: 增加结构静态断言**

  检查顶部没有独立锁定按钮、账户 XAML 没有 `ContextFlyout` 或独立复制图标按钮、左滑仍包含三个动作、锁定页包含电脑密码入口。

- [ ] **Step 2: 执行全量构建与回归**

  ```powershell
  dotnet test artifacts/windows/LiubaiMfa.Core.Regression/LiubaiMfa.Core.Regression.csproj --configuration Release
  dotnet test artifacts/windows/LiubaiMfa.Windows.Platform.Regression/LiubaiMfa.Windows.Platform.Regression.csproj --configuration Release
  dotnet build artifacts/windows/LiubaiMfa.sln --configuration Release --no-restore
  ```

- [ ] **Step 3: 执行人工 UI 验收**

  在 Windows 11 x64 运行：验证浅色/深色主题、窗口缩放、点击账户复制 1 秒反馈、向左拖动三项操作、右键无菜单、设置菜单备份入口、Hello 可用与不可用两种锁定路径；截图只作为视觉记录，不替代真实交互验收。

- [ ] **Step 4: 提交最终验证记录**

  更新 `artifacts/windows/VERIFICATION.md`，只记录实际执行过的命令、返回结果和未验证项；不记录验证码、密钥、密码或令牌。

