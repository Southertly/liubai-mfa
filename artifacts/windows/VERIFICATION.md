# Windows 版本验证记录

日期：2026-08-20

## 已自动验证

以下命令均在 Windows 11 x64 开发环境执行：

```powershell
dotnet restore .\LiubaiMfa.sln
```

结果：退出代码 0；所有项目均是最新的，无法还原。

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

结果：退出代码 0；5/5 项 Windows 核心安全检查通过（RFC 6238、`otpauth://totp`、移动端 v2 加密固定样本、备份往返与篡改拒绝、备份输入限制）。

```powershell
dotnet run --project .\LiubaiMfa.Windows.Platform.Regression\LiubaiMfa.Windows.Platform.Regression.csproj -c Release
```

结果：退出代码 0；86/86 项 Windows 平台回归检查通过。

```powershell
dotnet build .\LiubaiMfa.Windows.CodeBehindCheck\LiubaiMfa.Windows.CodeBehindCheck.csproj -c Release
```

结果：退出代码 0；0 警告、0 错误。

```powershell
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

结果：退出代码 0；真实 WinUI XAML 编译完成，0 警告、0 错误。

本轮自动验证已覆盖：启动保护设置的缺省值、类型和写入；开关策略；激活/失活时序与竞态；现有解锁会话、剪贴板和备份路径的回归；以及完整解决方案的 XAML 编译。

## MSIX 构建、安装与启动

- 只读检查在当前用户证书库中找到了 `CN=LiubaiMFA-Development` 开发证书，且证书具有私钥。
- 执行 `./scripts/build-msix.ps1 -CertificateThumbprint <现有证书指纹>` 后生成 `AppPackages\LiubaiMfa.Windows_1.0.0.2_x64_Test\LiubaiMfa.Windows_1.0.0.2_x64.msix`。
- 打包命令退出代码为 0，但有 1 条警告：未找到 `mspdbcmf.exe`，因此未生成符号包；未出现错误。
- `Add-AppxPackage -ForceApplicationShutdown` 未能安装刚生成的包：系统已有相同版本、但内容不同的包，返回 `0x80073CFB`。未移除或替换任何已安装包。
- `Get-AppxPackage -Name 'com.mfaverifier.app.windows'` 确认现有包状态为 `Ok`，版本为 `1.0.0.2`；已请求启动该已安装包并观察到应用进程。由于安装被拒绝，这不能证明刚生成的 MSIX 已安装成功。

## 待真人确认

- 设置关闭后重启：主界面前不出现 Windows Hello。
- 添加账户：不出现 Windows Hello。
- 开启启动保护：分别确认取消与成功路径。
- 关闭启动保护：分别确认取消与成功路径。
- 启动保护关闭时离开 60 秒不锁定；开启时离开 60 秒后锁定。
- 真实敏感操作（查看/复制 MFA 原始密钥、导出备份）触发 Windows Hello。
- 若需验收刚生成的 MSIX，先在不影响现有用户包的测试环境中处理同版本冲突，再验证安装、启动、升级和卸载。

## 当前不在首个 Spike 内

- 摄像头实时扫码；当前实现是从二维码图片导入。
- Windows 10、ARM64、云同步和 Microsoft Store 正式提交。
