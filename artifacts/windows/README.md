# 留白 MFA Windows

Windows 正式版面向 Windows 11 x64，使用 Electron 界面和 C# / .NET 8 本地核心。[Windows 1.0.4 发布页](https://github.com/Southertly/liubai-mfa/releases/tag/windows-v1.0.4)

安装、数据位置、应用内更新和打包见 [Windows 客户端说明](LiubaiMfa.Desktop/README.md)，实机回归见 [Windows 验证记录](VERIFICATION.md)。

## 工程结构

- `LiubaiMfa.Desktop` 提供 Electron 界面、应用内更新和 Windows 安装包构建。
- `LiubaiMfa.CoreHost` 负责账户库、分组、加密备份、识别二维码、应用锁和通信。
- `LiubaiMfa.Windows.Native` 封装 Windows 身份验证、DPAPI、剪贴板和二维码解码。
- `LiubaiMfa.Core` 提供 TOTP、URI 解析、备份加密和数据模型。
- `LiubaiMfa.Core.Regression` 验证验证码和备份格式。

在 Windows 11 x64 和 .NET 8 SDK 环境下，从此目录构建 C# 工程并运行核心回归。

```powershell
dotnet build .\LiubaiMfa.sln -c Release -p:Platform=x64
dotnet run --project .\LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj -c Release
```

Electron 的开发运行、正式 MSI 和预览 MSIX 使用各自的构建入口。[桌面端构建命令](LiubaiMfa.Desktop/README.md)
