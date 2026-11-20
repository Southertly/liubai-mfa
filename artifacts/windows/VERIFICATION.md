# Windows 版本验证记录

## Electron 正式版 1.0.0

验证日期为 2026-11-20，系统为 Windows 11 x64。发布的是未签名 MSI。[Windows 1.0.0 发布页](https://github.com/Southertly/liubai-mfa/releases/tag/windows-v1.0.0)

- 使用临时账户库验证含中文的自定义安装目录、启动与重启，并核对卸载 MSI 后账户库文件完整。
- 正式安装后验证预览版账户库迁入、重启后账户可见、验证码继续刷新。卸载预览版后再次校验正式版账户库和保留的备份副本。
- 核对正式安装包的远端 SHA-256 与本地产物一致，并确认正式版检查更新能识别已发布的 1.0.0。

## 旧版 WinUI 3 验证记录

日期：2026-08-03

### 已验证

- `LiubaiMfa.Core.Regression`：5 项通过。
  - RFC 6238 SHA-1 TOTP 官方测试向量；
  - `otpauth://totp` 解析与非 OTP 地址拒绝；
  - 与移动端 CryptoJS 4.2.0 一致的固定 IV、密文和 HMAC；
  - v2 加密备份往返、错误密钥和篡改拒绝；
  - 非法账户、超大备份限制。
- `LiubaiMfa.Windows.Platform`：Release 交叉编译通过，0 警告、0 错误。
- `LiubaiMfa.Windows.CodeBehindCheck`：App/MainWindow C# 代码通过，0 警告、0 错误。
- C# 空白格式检查通过。
- NuGet 对平台层直接及传递依赖的漏洞检查未发现已知漏洞。
- XAML、项目文件和清单均为结构正确的 XML。
- `Package.appxmanifest` 未声明网络、麦克风、摄像头、全文件系统或受限能力。
- 新增源码未发现输出 MFA 密钥或恢复密钥的日志。

### 当时待 Windows 11 x64 验证

- 官方 WinUI XAML 编译器完整构建；
- Windows Hello 可用、取消、失败和重试路径；
- DPAPI 在重启、切换 Windows 用户及本地数据损坏后的行为；
- 二维码图片选择与实际解码；
- Android 与 Windows 真实备份文件双向导入；
- 开发证书签名的 MSIX 生成、安装、启动、升级和卸载；
- 应用离开 60 秒后的锁定界面和敏感信息遮挡。

该次验证在非 Windows 环境进行，结论限于当时的交叉编译和核心测试。

### 当时不在原型验证范围内

- 摄像头实时扫码；当前实现是从二维码图片导入。
- Windows 10、ARM64、云同步和 Microsoft Store 正式提交。
