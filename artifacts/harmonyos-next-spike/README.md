# 留白 MFA for HarmonyOS NEXT

HarmonyOS NEXT 原生实现，使用 ArkTS、ArkUI 和系统安全能力。它与 `artifacts/mobile` 的 Expo / React Native 客户端并行维护，但不能作为 Expo 构建目标。

当前源码配置的 Bundle ID 为 `com.sediary.liubaimfa`，版本为 `1.1.0`。实际签名和上架状态不由这份源码证明，均需以当前构建产物和 AppGallery Connect 为准。

## 分发与安装

GitHub Release 不提供 HarmonyOS HAP/APP。正式安装包只应通过受控渠道或 AppGallery 分发；源码、构建成功和本地正式签名不能证明应用已经提交审核或上架。

覆盖升级前必须确认 Bundle ID、发布签名保持一致且版本号递增。调试签名与正式签名不能互相覆盖；签名不一致时应先导出加密备份并单独保存恢复密钥，不得直接卸载或清除数据。

## 范围与状态

下列能力已在当前源码中实现：账户分组与颜色、TOTP、手动和 `otpauth://` 导入、系统 Scan Kit 扫码、Asset Store 保护密钥、系统身份认证、加密备份的导入/导出、与 Android 版双向二维码安全迁移，以及浅色/深色/跟随系统主题。

系统认证、扫码、文件选择、系统分享和 Asset Store 的真实行为依赖目标设备与系统版本；必须在目标模拟器或真机复测。尤其不能由源码推断已完成 AppGallery 发布、正式签名或系统分享的端到端验收。

## 源码能力

- TOTP 账户列表、搜索、分组和颜色标签。
- 手动添加、`otpauth://` 导入和系统 Scan Kit 扫码。
- 账户编辑与删除。
- Asset Store 加密保存 MFA 密钥。
- User Authentication Kit 保护验证码解锁、密钥查看/复制和备份导出。
- 与 Android 版兼容的 v2 加密备份导出与导入；恢复密钥与备份内容分开保存。
- 与 Android 版兼容的双向二维码安全迁移：系统认证后使用 5 分钟有效的一次性 RSA-OAEP 公钥配对，账户内容继续使用带完整性校验的加密封装；相同密钥跳过且不覆盖。
- 浅色、深色及跟随系统主题。
- 本地隐私说明与应用锁。
- 隐私窗口保护，系统截图和最近任务预览不显示账户内容。
- 验证码复制 30 秒后自动清除；密钥和恢复密钥复制 60 秒后自动清除。

应用申请 `ohos.permission.ACCESS_BIOMETRIC` 和 `ohos.permission.PRIVACY_WINDOW`，不申请网络或剪贴板读取权限。MFA 密钥不会写入普通偏好设置、日志或测试输出；复制内容仅限本机剪贴板，并在用户未覆盖剪贴板时按时自动清除。

## 构建

优先使用 DevEco Studio 自带的 Hvigor。日常调试继续使用 `default` 产品和调试签名：

```bash
/Applications/DevEco-Studio.app/Contents/tools/hvigor/bin/hvigorw \
  assembleApp -p product=default -p buildMode=debug --no-daemon
```

调试签名 HAP 输出位置：

```text
entry/build/default/outputs/default/entry-default-signed.hap
```

`release` 产品用于配置发布构建；执行前必须在本机 `build-profile.json5` 中配置有效的发布签名：

```bash
/Applications/DevEco-Studio.app/Contents/tools/hvigor/bin/hvigorw \
  assembleApp -p product=release -p buildMode=release --no-daemon
```

发布构建产物的预期输出位置：

```text
entry/build/release/outputs/default/entry-default-signed.hap
build/outputs/release/harmonyos-next-spike-release-signed.app
```

`build-profile.json5` 包含本机 DevEco 调试和发布签名配置，应保持在本地；发布私钥、证书和 Profile 不得提交到仓库。可共享配置以 `build-profile.example.json5` 为准。构建成功只证明本地产物可用，不等同于 AppGallery 发布完成。

## 真机安装

连接并授权设备后，使用 DevEco Studio 运行，或通过 DevEco 自带的 `hdc` 覆盖安装：

```bash
/Applications/DevEco-Studio.app/Contents/sdk/default/openharmony/toolchains/hdc \
  -t <device-id> install -r entry/build/default/outputs/default/entry-default-signed.hap
```

只有 Bundle ID 和签名均一致时，覆盖安装才会保留已有账户。不要用真实账户执行破坏性恢复测试。

## 从开发包迁移

早期开发包可能使用不同 Bundle ID 或调试签名，系统不会自动迁移账户。迁移时保留旧包，通过旧包导出加密备份，再在正式包中输入单独保存的恢复密钥完成导入；确认账户数量与验证码正常后，再由用户决定是否卸载旧包。
