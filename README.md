# 留白 MFA

跨平台 MFA 验证器工作区。Android 客户端是当前优先路径；HarmonyOS NEXT 目录提供 ArkTS / ArkUI 原生实现，不能把它当作 Expo 的构建目标。

## 当前范围

- `artifacts/mobile`：Expo SDK 54 / React Native Android 客户端；应用包名为 `com.mfaverifier.app`。
- `artifacts/harmonyos-next-spike`：HarmonyOS NEXT 原生实现；Bundle ID 为 `com.sediary.liubaimfa`。
- `lib` 与 `artifacts/api-server`：工作区共享 API 与服务端代码；不属于 MFA 密钥存储或备份的权威实现。

源码已实现本地账户加密、系统身份认证后的密钥查看/复制、带独立恢复密钥的 v2 加密备份，以及 Android 与 HarmonyOS 之间的双向二维码安全迁移。Android 使用 Expo SecureStore 保存设备加密密钥；HarmonyOS 使用 Asset Store 与系统认证能力。两端都不应实现或保存自定义手机系统 PIN。

## 运行与验证

标准验证命令（在仓库依赖可完整解析时）为：

```bash
pnpm --filter @workspace/mobile run typecheck
pnpm --filter @workspace/scripts exec tsx ../artifacts/mobile/scripts/security-regression.ts
```

截至 2026-07-21，以上 pnpm 命令会在执行前因 HarmonyOS 工作区依赖 `@ohos/hvigor-ohos-plugin` 不在 npm registry 而被阻断。这是工作区依赖解析问题，不等同于移动端类型或安全回归失败；在该依赖来源修复前，应把完整门禁结果标为待确认，不能宣称通过。

HarmonyOS NEXT 构建和真机安装说明见 [HarmonyOS README](artifacts/harmonyos-next-spike/README.md)。涉及系统认证、扫码、文件选择、分享或安全存储的结论，必须在目标模拟器或真机验证；网页预览不能作为原生安全能力的证据。

## 安全边界

- MFA 密钥不得以明文写入普通存储、日志或测试输出。
- 查看/复制密钥与导出备份前必须进行系统身份验证。
- 备份必须加密，恢复密钥不得嵌入备份内容。
- 二维码迁移必须先通过系统身份验证；配对码只包含 5 分钟有效的一次性公钥，账户码只包含加密数据。完成、取消或超时后清除接收端临时私钥。
- 二维码导入按规范化后的 MFA 密钥去重；相同密钥只跳过，不覆盖已有账户。
- 复制恢复密钥后，才允许打开系统分享页。
- 当前阶段只做本地手动备份；不实现 Google Drive 或 iCloud 自动备份，除非需求重新确认。
- Android 明确关闭系统自动备份，跨设备恢复只使用应用内的加密备份，避免“账户密文被恢复但本设备密钥无法恢复”的断链状态。

## Android 正式签名与覆盖升级

- 正式 APK 必须使用长期保存的同一把签名密钥；签名文件、口令不得进入 Git。
- 本机正式签名由 `artifacts/mobile/scripts/build-signed-android.sh` 从 macOS 钥匙串读取口令。
- 覆盖升级前运行 `artifacts/mobile/scripts/verify-android-upgrade.sh <旧 APK> <新 APK>`；脚本会先核对签名和版本号，不一致时停止，绝不卸载应用或清除数据。
- 每次发布仍需在专用测试账户上核对覆盖升级后的账户数量和验证码；源码和构建成功不能替代该真机验收。

## 状态说明

仓库源码和测试只能证明本地实现；不能单独证明 AppGallery 发布、正式签名或最新真机验收。此类状态为待确认，应以当前构建产物、签名配置和真实设备验证为准。
