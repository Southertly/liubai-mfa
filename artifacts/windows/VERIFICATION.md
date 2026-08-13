# Windows 版本验证记录

日期：2026-08-03

## 已验证

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

## 待 Windows 11 x64 验证

- 官方 WinUI XAML 编译器完整构建；
- Windows Hello 可用、取消、失败和重试路径；
- DPAPI 在重启、切换 Windows 用户及本地数据损坏后的行为；
- 二维码图片选择与实际解码；
- Android 与 Windows 真实备份文件双向导入；
- 开发证书签名的 MSIX 生成、安装、启动、升级和卸载；
- 应用离开 60 秒后的锁定界面和敏感信息遮挡。

当前开发机不是 Windows，无法运行微软提供的 `XamlCompiler.exe`、`MakePri.exe` 或 MSIX 安装流程。因此不得把交叉编译结果描述为 Windows 真机验收通过。

## 当前不在首个 Spike 内

- 摄像头实时扫码；当前实现是从二维码图片导入。
- Windows 10、ARM64、云同步和 Microsoft Store 正式提交。
