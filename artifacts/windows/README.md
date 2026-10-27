# 留白 MFA Windows

这是与 Android、HarmonyOS NEXT 分离的 Windows 11 x64 原生客户端。它使用 WinUI 3 / C#，复用现有 TOTP、`otpauth://`、账户字段和 v2 加密备份规则，但不复用 Expo 的安全存储、系统认证、相机、剪贴板或分享实现。

## 已实现范围

- 标准 TOTP 验证码与 30 秒倒计时。
- 手动添加、编辑、删除和分组字段。
- 从 PNG/JPEG/BMP 二维码图片导入 `otpauth://totp` 账户。
- 当前 Windows 用户范围保护的设备密钥，以及加密的本地账户文件。
- Windows Hello 启动解锁、60 秒离开后重新锁定、查看/复制密钥和导出备份前再次验证。
- 与移动端一致的 v2 加密备份、独立恢复密钥、去重导入和篡改拒绝。
- Windows 11 x64 单项目 MSIX 配置与开发签名脚本。

应用不请求网络能力，不实现云同步，也不读取或保存 Windows 系统 PIN。MSIX 仅声明 WinUI 3 桌面入口所需的 `runFullTrust`，不声明麦克风、摄像头或全文件系统能力。

## 工程结构

- `LiubaiMfa.Core`：不依赖 Windows 的 TOTP、URI 解析、v2 备份加解密和数据模型。
- `LiubaiMfa.Core.Regression`：RFC 6238、移动端格式互通、往返恢复和篡改拒绝回归测试。
- `LiubaiMfa.Windows.Platform`：可独立编译检查的 Windows Hello、DPAPI、文件、二维码和 ViewModel 服务层。
- `LiubaiMfa.Windows.CodeBehindCheck`：不替代 XAML 编译，仅在非 Windows 环境检查 App/MainWindow C# 代码。
- `LiubaiMfa.Windows`：WinUI 3 界面、Windows Hello、受保护设备密钥、文件选择和二维码读取。
- `scripts`：Windows 开发证书与测试 MSIX 构建脚本。

## Windows 构建环境

需要一台 Windows 11 x64 电脑或虚拟机，并安装：

1. Visual Studio 的 Windows 应用开发工作负载；
2. .NET 8 SDK；
3. Windows 11 SDK；
4. 已配置的 Windows Hello（系统 PIN、指纹或面容之一）。

先运行核心回归：

```powershell
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

再构建 Windows 工程：

```powershell
dotnet restore .\LiubaiMfa.sln
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
```

## 生成测试 MSIX

开发证书只用于本地测试，不是 Microsoft Store 正式签名：

```powershell
.\scripts\new-development-certificate.ps1
.\scripts\build-msix.ps1 -CertificateThumbprint <上一步输出的 Thumbprint>
```

生成证书后，脚本会导出不含私钥的 `.cer`。安装测试 MSIX 前，需要由测试人员手动信任该公开证书。证书、私钥和生成的安装包均被视为本地材料，不应提交到仓库。

## 安全与数据位置

- 本地数据目录：`%LOCALAPPDATA%\Liubai MFA`。
- `device-key.v1.bin`：使用 Windows DPAPI 按当前用户保护的随机设备密钥。
- `accounts.v2.json`：使用上述设备密钥加密后的账户数据；不应包含明文 MFA 密钥。
- 导出备份：AES-256-CBC + HMAC-SHA-256 的 v2 格式，恢复密钥不会写入备份文件。
- 本地数据无法解锁时，应用保持锁定并停止写入，避免用空数据覆盖原文件。

## 验收边界

以下结果必须在 Windows 11 x64 环境验证，macOS 上的源码或核心测试不能替代：

- WinUI 工程编译；
- Windows Hello 系统界面及取消/失败路径；
- DPAPI 在重启、换用户后的行为；
- 二维码图片选择与解析；
- MSIX 签名、安装、启动、升级和卸载；
- 实际备份文件与 Android 端双向导入。

摄像头实时扫码尚未纳入首个可验证 Spike；当前支持二维码图片导入。正式 Microsoft Store 发布、正式证书、Windows 10 和 ARM64 也不在本阶段范围内。
