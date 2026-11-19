# Android Expo SDK 54 补丁维护与本地验收

日期：2026-11-19。基于 `50c85af` 完成本地验收，源码通过 Pull Request 提交至 GitHub；应用版本保持 1.1.1 / versionCode 3，不发版、不替换 GitHub Release 的安装包。

## 依赖范围

| 直接依赖 | 原安装版本 | 本地目标版本 |
| --- | --- | --- |
| expo | 54.0.35 | 54.0.37 |
| expo-local-authentication | 17.0.8 | 17.0.9 |
| expo-constants | 18.0.13 | 18.0.14 |

这三个依赖使用精确版本锁定，删除开发依赖中重复的 `expo` 声明。锁文件同时记录 Expo 要求的传递依赖和 peer 依赖快照；`expo-asset` 对旧 constants 的引用通过定向更新 `expo-constants` 统一到 18.0.14。未升级 Expo SDK 大版本、React 或 React Native。

SDK 54 官方发布说明：

- [expo 54.0.37](https://github.com/expo/expo/blob/sdk-54/packages/expo/CHANGELOG.md)：Android fetch 数据块顺序修复、TextDecoder 优化。
- [expo-local-authentication 17.0.9](https://github.com/expo/expo/blob/sdk-54/packages/expo-local-authentication/CHANGELOG.md)：修复 Android 并发认证调用错误结束当前提示的问题。
- [expo-constants 18.0.14](https://github.com/expo/expo/blob/sdk-54/packages/expo-constants/CHANGELOG.md)：未引入用户可见变更。

## 原生配置维护方式

Android 原生目录由仓库手动维护，不使用 Prebuild 重新生成。`app.json` 仍用于 Expo 运行时配置及声明原生意图，修改后必须同步核对原生实现；config plugins 的声明不会在 Gradle 构建时自动重放。iOS 当前没有对应原生工程，本次不做 iOS 构建或配置迁移。

已使用 Expo public config、原生 XML / Gradle / Kotlin 文件进行核对：

| 核对项 | 当前原生实现与处理 |
| --- | --- |
| 名称、包名、版本 | 留白 MFA、com.mfaverifier.app、1.1.1 / 3；与 app.json 一致 |
| 屏幕方向与深链 | MainActivity 为 portrait；保留 tickmfa scheme |
| 主题与新架构 | 主题 automatic；DayNight 原生主题；newArchEnabled=true |
| 图标、启动屏及状态栏 | 保留现有 mipmap / drawable 资源；浅深色启动屏及状态栏配置保留 #0d0f14；未替换图片或样式 |
| 相机和受限权限 | 相机保留；INTERNET、ACCESS_NETWORK_STATE、RECORD_AUDIO、读写外部存储、SYSTEM_ALERT_WINDOW 继续使用 manifest remove 规则 |
| 系统备份 | allowBackup=false；保留数据保护插件与原生设置 |
| 防截图 | MainActivity 的 FLAG_SECURE 保留 |
| 原生安全模块 | QrTransferCryptoPackage、SensitiveClipboardPackage 注册和实现保留 |
| 正式签名 | 继续从环境及钥匙串获取；缺少正式签名时阻止 Release 打包；不回退调试签名 |
| 编译路径保护 | C / C++ ffile-prefix-map 保留 |
| 系统认证及安全存储 | LockContext、secureStorage、加密及备份逻辑未改动 |

本次没有发现需要修改上述原生源码的配置差异，也没有运行 Prebuild。

`package.json` 的 `expo.doctor.appConfigFieldsNotSyncedCheck.enabled` 设置为 false，明确采用手动维护原生工程。[Expo 官方配置说明](https://docs.expo.dev/versions/latest/config/package-json/#appconfigfieldsnotsyncedcheck)。这项是关闭通用提醒，原生一致性由上述核对承担；其他 Doctor 检查保持启用。

以后修改 app.json、插件或相关依赖时，需要重新核对本表、检查原生文件差异、运行标准类型 / 安全门禁及 Doctor，并构建和验证对应原生交互。

## 验证记录

| 检查 | 最终结果 |
| --- | --- |
| `pnpm install --frozen-lockfile` | 通过；锁文件与依赖声明一致 |
| `pnpm run typecheck` | 通过，覆盖整个工作区 |
| `pnpm --filter @workspace/mobile run typecheck` | 通过 |
| `pnpm --filter @workspace/scripts exec tsx ../artifacts/mobile/scripts/security-regression.ts` | 通过；实际执行安全回归脚本 |
| `pnpm dlx expo-doctor@latest artifacts/mobile --verbose` | Doctor 1.21.8，17/17 启用检查通过；已关闭的原生同步提醒另由上表核对，不计入通过数 |
| 原生构建 | 原有签名脚本 `app:assembleRelease` 成功；保留四种 ABI，未执行 Prebuild |
| 测试 APK 与已发布 APK 比较 | 包名、版本、签名证书和权限集合一致；已发布 APK 摘要不变 |
| 测试 APK manifest | allowBackup=false；受限权限未合入；tickmfa scheme 保留 |
| 运行时原生窗口 | SECURE 标志保留 |
| 原生 / 安全源码差异 | `android`、`plugins`、`context`、`utils`、`assets` 相对基线无已跟踪文件差异 |

Doctor 首轮发现 `expo-asset` 仍指向 constants 18.0.13；定向更新该依赖后冻结安装，并重跑类型、安全、Doctor 及原生构建，重复原生模块检查通过。

## 模拟器验收

使用已有专用 Android API 36 模拟器。从已发布版本执行同版本 `adb install -r` 返回 Success，未卸载应用或清除数据。

- 原合成测试账户 UpgradeSentinel 保留，验证码跨周期正常刷新。
- 确认原模拟器没有锁屏凭据后，使用 Android 系统测试工具临时设置专用测试 PIN；应用未增加任何自定义 PIN 功能。
- 开启应用锁、立即锁定，进入 Android 系统身份认证；系统 PIN 验证成功，回到首页。
- 编辑合成测试账户时，密钥初始隐藏。点击查看进入系统认证，取消认证后仍保持隐藏；重新认证成功后可查看，随后再次隐藏，未复制密钥或修改账户。
- 恢复应用锁关闭状态并移除临时系统锁屏凭据；验证无凭据状态恢复。停止并重新启动应用，原合成账户仍可读取，验证码正常刷新。
- 系统 PIN 页属于 Android 系统界面。本次未使用真实账户、读取真实密钥、导出备份或选择分享接收方。

## 本地产物与边界

测试 APK、`APK-INFO.json` 和 `SHA256SUMS.txt` 位于忽略目录 `release/android/local-expo54-patch-2026-11-19/`，测试 APK 名称为 `Liubai-MFA-Android-1.1.1-local-expo54-patch.apk`。

- 测试 APK SHA-256：`57570b6f355b091bab6a50b109be1bb183d6d2dbb1e0888bb3e03af7e2110493`。
- 已发布 1.1.1 APK SHA-256 保持 `a62e8002fc654d58905a142f3af1d66f18eaa1210679a21c731a92906ff97257`。
- `.gitignore` 补充 Kotlin 生成缓存目录规则，构建缓存不纳入源码。

本次仅提交依赖、配置及验证文档，经 Pull Request 合并；未递增版本、创建标签、修改 Release 或上传安装包，本地测试产物不纳入提交。未进行真机厂商指纹 / 面容、真实相机、备份完整 UI、双设备迁移及并发认证竞态的独立专项测试；没有将模拟器 PIN 验收扩大为这些流程已通过。
