# Windows 版本验证记录

日期：2026-08-20

## 已自动验证

以下命令均在 Windows 11 x64 开发环境执行：

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

结果：退出代码 0；5/5 项 Windows 核心安全检查通过。

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

结果：退出代码 0；92/92 项 Windows 平台回归检查通过。覆盖启动保护缺省关闭、开关验证、激活/失活自动锁定、账户筛选和写入竞态、会话取消、备份边界、紧凑账户卡片、临期警示、窗口布局、剪贴板失败策略等行为。

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release -p:Platform=x64
dotnet build .\LiubaiMfa.Windows\LiubaiMfa.Windows.csproj -c Release -p:Platform=x64
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

结果：三项构建均退出代码 0，0 警告、0 错误；真实 WinUI XAML 编译完成。

生成代码已确认 `ExpiryAlertOpacity` 的属性通知通过 `x:Bind` 直接写入临期警示进度条的 `Opacity`，不再经过 `StateTrigger`、隐式 `DataContext` 或 `ElementName` 中转。

## 启动崩溃与临期颜色修复

- 初版 DataTemplate 内的 `StateTrigger` 使用 `x:Bind IsExpiring`，真实包可稳定复现启动崩溃：`Microsoft.UI.Xaml.dll`、`0xc000027b`，WER 内层为 `combase.dll`、`E_POINTER (0x80004003)`。
- 单变量包将该触发器固定为 `False` 后连续三次稳定启动且无新 WER，确认崩溃源位于该触发绑定。
- 改为无 Source 的普通 `Binding` 后不再崩溃，但真实界面在 4 秒时仍保持蓝色；原因是 `StateTrigger` 不在模板项的可视树中，无法继承项 `DataContext`。
- 改为 `ProgressBar.Tag` 加 `ElementName` 后，生成代码能更新 `Tag`，但真实界面在 4 秒时仍保持蓝色；失效点进一步收敛到脱离可视树的 `StateTrigger` 对模板名称域的监听。
- 最终实现移除该视觉状态链路，使用普通 Accent 进度条和主题感知的 Critical 覆盖层；覆盖层由生成的 `x:Bind` 直接控制 0/1 透明度，不拦截输入，也不在常用无障碍树中重复暴露进度语义。
- 自动回归覆盖：6 秒时警示层透明、5–1 秒显示、1→30 秒周期重置后恢复透明，并验证进入和退出临期均发送属性通知。

## MSIX 构建、签名、升级与启动

- 正式包已从 `1.0.0.6` 原位升级到 `1.0.0.7`，未卸载应用。
- 正式包状态为 `Ok`，签名验证为 `Valid`；MSIX SHA-256：`1C47A4117A615F55DBBC4361E3086D92AA589799D787EDAE068873A3D5E04955`。
- 正式包连续三次无输入启动稳定，第三次实例保持运行；本轮新增包级 Application Error/WER 为 0。
- 正式包升级前后的账户密文和设备密钥均为 `HashUnchanged=True`。
- 独立假数据视觉验收包已原位升级到 `visualqa 1.0.0.2`，状态为 `Ok`，签名验证为 `Valid`；MSIX SHA-256：`A74215ECC12E417A95F566A3037CE5C7B17ADED9519BE0D282F36B80C17BE36B`。
- 正式包与 QA 包的 57 个非 manifest 载荷逐文件哈希一致；QA 包只使用独立身份和假账户数据，升级前后假数据 `HashUnchanged=True`。
- 最终视觉验收完成后已卸载独立 QA 包及其假数据；正式包仍为 `1.0.0.7`、状态 `Ok` 且窗口正常响应。
- 打包仅有缺少 `mspdbcmf.exe` 的符号包警告，不影响 MSIX 生成、签名、安装或启动。
- 本地打包读取了用户保留的、未提交的 `Package.appxmanifest`；该文件未被暂存或提交。

## 已完成的真实界面检查

- 默认关闭启动保护时打开应用：未出现 Windows Hello。
- 添加假账户：未出现 Windows Hello。
- 账户密钥输入：`PasswordBox` 默认遮挡，显示查看按钮；对话框结束时清空输入。
- 空状态、搜索框、分组计数、柔和账户卡片、复制按钮和底部说明已在 QA 包中显示正常。
- 假账户在普通倒计时区间显示 Accent 蓝色进度条。
- 假账户在剩余 5 秒时显示 Critical 红色进度条；进入新周期后恢复 Accent 蓝色，最终像素检查通过。

## 仍待人工或环境确认

- 启动保护开关的 Windows Hello 取消、成功、不可用、失败和重试路径。
- 已开启启动保护后的启动解锁、手动锁定、重新解锁和离开 60 秒自动锁定。
- 查看/复制 MFA 原始密钥、导出备份时的 Windows Hello 取消和成功路径。
- DPAPI 在重启、切换 Windows 用户及本地数据损坏后的行为。
- 真实二维码图片选择与解码；Android 与 Windows 真实备份文件双向导入。
- 深色、高对比主题以及 480×620 实际窗口尺寸的最终人工视觉检查。

## 当前不在首个版本内

- 摄像头实时扫码；当前实现是从二维码图片导入。
- Windows 10、ARM64、云同步和 Microsoft Store 正式提交。
