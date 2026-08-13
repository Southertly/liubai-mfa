# 留白 MFA

留白 MFA 是一套本地优先的跨平台身份验证器。项目当前包含 Android、HarmonyOS NEXT 和 Windows 11 客户端源码，核心目标是离线生成 TOTP 验证码，并通过系统安全能力保护账户密钥、备份和跨设备迁移。

项目不提供云同步，不读取手机系统 PIN，也不把 MFA 密钥发送到服务器。

## 平台状态

| 平台           | 实现                                      | 分发状态                                   | 验证边界                                                                            |
| -------------- | ----------------------------------------- | ------------------------------------------ | ----------------------------------------------------------------------------------- |
| Android        | Expo SDK 54 / React Native + 原生安全模块 | `v1.1.0` APK 已发布                        | 正式构建、签名、类型检查和安全回归已完成；当前 Release APK 的最新真机安装验收待补跑 |
| HarmonyOS NEXT | ArkTS / ArkUI 原生客户端                  | 源码版本 `1.1.0`，不在 GitHub 发布 HAP/APP | 正式构建与签名可在本机完成；AppGallery 上架及正式包迁移验收待确认                   |
| Windows 11 x64 | WinUI 3 / C# MVP                          | 暂无安装包                                 | 核心源码已实现；XAML、Windows Hello、DPAPI 和 MSIX 必须在 Windows 11 x64 验证       |

源码、构建成功和历史测试不能单独证明应用市场已上架或当前设备已完成验收。运行状态以对应平台的最新产物和真实设备结果为准。

## 下载

Android 正式安装包位于 [GitHub Releases](https://github.com/Southertly/mfa-verifier/releases)：

- `Liubai-MFA-Android-1.1.0.apk`
- `SHA256SUMS.txt`

当前 Release 不提供 HarmonyOS 或 Windows 安装包。

下载后可校验完整性：

```bash
shasum -a 256 -c SHA256SUMS.txt
```

## 主要功能

- 标准 TOTP 验证码、倒计时、搜索和账户分组。
- 手动添加、编辑、删除及 `otpauth://` 二维码导入。
- 常用服务图标和服务名称优先展示。
- 系统身份认证保护应用锁、密钥查看/复制和备份导出。
- 设备安全存储保护本地加密密钥，账户密钥不写入普通偏好设置。
- v2 加密备份，恢复密钥与备份文件分开保存。
- Android 与 HarmonyOS 双向二维码迁移；相同密钥跳过且不覆盖新设备已有账户。
- 隐私窗口保护截图和最近任务预览。
- 验证码、密钥和恢复密钥复制后的定时剪贴板清理。
- 浅色、深色和跟随系统主题。

当前只支持 TOTP；HOTP 尚未实现。

## 安装与升级

### Android

Android 正式包名为 `com.mfaverifier.app`，最低支持 Android 7.0（API 24）。

同包名且使用同一正式签名的后续版本可以覆盖升级，升级时不要卸载旧应用或清除数据。早期 `1.0.0` 内部测试包使用 Android Debug 签名，不能直接覆盖为正式版；迁移前必须先导出加密备份，并单独保存恢复密钥。

发布前可检查两个 APK 是否满足覆盖升级条件：

```bash
artifacts/mobile/scripts/verify-android-upgrade.sh <旧版.apk> <新版.apk>
```

### HarmonyOS NEXT

HarmonyOS Bundle ID 为 `com.sediary.liubaimfa`。正式包只应通过受控渠道或 AppGallery 分发，GitHub 不提供 HAP/APP。

调试签名与正式签名不能互相覆盖。切换签名之前必须从旧应用导出加密备份；确认备份文件和恢复密钥均已保存后，才能迁移到正式版。详细构建和设备说明见 [HarmonyOS README](artifacts/harmonyos-next-spike/README.md)。

### Windows

Windows 版本目前是源码 MVP，尚未提供可分发 MSIX。构建要求和验收边界见 [Windows README](artifacts/windows/README.md)。

## 备份与二维码迁移

- 导出备份前必须再次通过系统身份认证。
- 备份采用加密封装，恢复密钥不会写入备份文件。
- 用户复制恢复密钥后，应用才允许打开系统分享页导出备份。
- 恢复时按规范化后的 MFA 密钥去重；相同密钥直接跳过，不覆盖已有账户。
- 二维码迁移使用 5 分钟有效的一次性公钥配对，账户数据以加密二维码传输。
- 多张二维码由发送端轮换展示，接收端可连续扫描；完成、取消或超时后清除临时私钥。
- 分组名称随账户迁移；同名分组合并，不使用分组颜色。

二维码迁移适合面对面的短距离迁移，不代替长期加密备份。

## 安全设计

- MFA 密钥不得以明文写入普通存储、日志或测试输出。
- Android 使用 Expo SecureStore 保存设备加密密钥，并关闭系统自动备份。
- HarmonyOS 使用 Asset Store 和系统身份认证能力保护密钥。
- Windows 使用当前用户范围的 DPAPI 保护设备密钥。
- 应用不读取、保存或实现自定义的手机或 Windows 系统 PIN。
- 签名证书、Profile、私钥、口令、真实账户和本机配置不得进入 Git 或 Release。
- 正式升级必须保持包标识和发布签名一致；签名不一致时停止，不通过卸载绕过数据保护。

安全问题的私密报告方式和支持范围见 [SECURITY.md](SECURITY.md)。

## 仓库结构

```text
artifacts/mobile/                  Android / Expo 客户端
artifacts/harmonyos-next-spike/   HarmonyOS NEXT 原生客户端
artifacts/windows/                Windows 11 x64 原生 MVP
lib/                              工作区共享库
scripts/                          回归与辅助脚本
```

`lib` 和 `artifacts/api-server` 不属于 MFA 密钥存储或备份的权威实现。各平台的安全存储和系统认证必须由对应原生客户端完成。

## 开发与验证

仓库使用 pnpm 工作区。标准移动端检查为：

```bash
pnpm --filter @workspace/mobile run typecheck
pnpm --filter @workspace/scripts exec tsx ../artifacts/mobile/scripts/security-regression.ts
```

当前完整 pnpm 门禁会在依赖解析阶段受到 HarmonyOS 专用依赖不在 npm registry 的 404 阻断。该阻断不等同于移动端检查失败，但在依赖来源修复前，完整工作区门禁仍标记为待确认；本地直接 TypeScript 检查不能替代完整门禁。

Android 正式构建从环境变量和 macOS 钥匙串读取签名信息：

```bash
artifacts/mobile/scripts/build-signed-android.sh
```

构建脚本不会把签名文件或口令写入仓库。涉及系统认证、相机扫码、安全存储、分享、安装和覆盖升级的结论必须在对应真实设备验证，网页预览不能作为证据。

## 发布规则

1. 更新版本号和 [CHANGELOG.md](CHANGELOG.md)。
2. 运行可用的类型、安全、构建和平台检查，并记录未完成的真机门禁。
3. 检查暂存内容及安装包，排除密钥、证书、账户数据、本机路径和设备信息。
4. 通过 Pull Request 合并到 `main`。
5. 从 `main` 对应提交创建版本标签和 GitHub Release。
6. 上传平台安装包和 `SHA256SUMS.txt`，再次核对远端文件摘要。

## 许可证

本仓库目前为私有项目，未授予开源使用、修改或再分发许可。除非仓库所有者另行书面授权，保留所有权利。
