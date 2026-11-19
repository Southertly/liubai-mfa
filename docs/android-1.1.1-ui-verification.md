# Android 1.1.1 界面对齐与验证记录

日期为 2026-11-19（北京时间）。本次按用户确认的任务包对齐 HarmonyOS 界面，递增安卓版本并生成正式签名 APK；用户验收界面后授权推送源码并发布 GitHub Release。

## 范围

参照当前 `artifacts/harmonyos-next-spike/entry/src/main/ets/pages/Index.ets` 和 `ui/ProductTheme.ets`。仓库内鸿蒙商店截图仅作辅助，其内容早于部分当前源码行为。

- 对齐浅深色配色、标题与返回按钮、搜索框、卡片、表单、标签颜色及操作弹窗。
- 覆盖首页、添加/编辑账户、备份与恢复、二维码迁移、扫码、隐私同意、应用锁及颜色/分组选择。
- 增加分组管理页面，调用原有 `renameGroup` / `removeGroup`，不新增数据格式。
- 编辑页使用现有 TOTP 生成函数展示当前验证码；密钥查看/复制仍经过原有认证入口。
- 保留 Android 粘贴导入备份、系统分享和应用内相机；没有引入鸿蒙文件选择器或系统 Scan Kit。
- 备份页增加与现有隐私同意页相同的隐私政策链接。
- 按用户后续反馈移除首页底部重复的扫码按钮，扫码入口保留在「＋ → 扫描二维码添加」，列表底部不再为浮动按钮预留额外空间。
- 未修改 AccountsContext、LockContext、安全存储、加密封装、备份解析、二维码协议及 Android 原生安全模块。未增加依赖、权限、云同步或数据迁移。

## 产物

- 包名 `com.mfaverifier.app`。
- versionName `1.1.1`，versionCode `3`；原正式 APK 为 `1.1.0 / 2`。
- 安装包 `release/android/v1.1.1/Liubai-MFA-Android-1.1.1.apk`。
- 校验文件 `release/android/v1.1.1/SHA256SUMS.txt`。
- 公开下载见 [GitHub Release v1.1.1](https://github.com/Southertly/liubai-mfa/releases/tag/v1.1.1)，仅分发正式签名 APK 与校验文件。
- 原始模拟器截图 `release/android/v1.1.1/screenshots/`。此目录和安装包按现有规则忽略，不进入 Git。
- 沿用原正式签名，从本机签名文件及钥匙串读取，未将私钥或口令写入仓库。

## 已执行检查

| 检查 | 结果 |
| --- | --- |
| 本机正式签名 Release 构建 | 通过；全架构配置保持不变 |
| 新旧 APK 包名、版本及签名证书比较 | 包名相同、2 → 3、证书一致 |
| `node artifacts/mobile/node_modules/typescript/bin/tsc -p artifacts/mobile/tsconfig.json --noEmit` | 通过；直接检查仅作诊断，不替代完整 pnpm 门禁 |
| `node --import ./scripts/node_modules/tsx/dist/loader.mjs artifacts/mobile/scripts/security-regression.ts` | 原安全回归脚本实际执行通过，不输出密钥 |
| `pnpm --filter @workspace/mobile run typecheck` | 未进入类型检查，被工作区 HarmonyOS `@ohos/hvigor` npm 404 阻断 |
| `pnpm --filter @workspace/scripts exec tsx ../artifacts/mobile/scripts/security-regression.ts` | 未进入安全脚本，同样被 HarmonyOS 依赖 404 阻断 |
| `pnpm dlx expo-doctor@latest artifacts/mobile` | 16/18 通过；Expo API TLS/网络失败，及现有原生目录与 app.json 同步提示 |
| `git diff --check` | 通过 |

版本号已同时更新 `app.json` 和原生 Gradle 配置，没有通过重新生成原生工程覆盖安全定制。

## 模拟器验证

设备为专用 `MFA_Upgrade_API_36`（Android API 36）。所有操作仅发生在模拟器，未连接或修改用户真机。

- 保留已有 `UpgradeSentinel` 测试账户，从正式 1.1.0 覆盖到 1.1.1，账户仍在，验证码随时间刷新。随后同版本覆盖更新最终构建。
- 原覆盖升级脚本已完成签名/版本检查及 `adb install -r`，安装返回 Success；其末尾 `monkey` 在该模拟器返回 251，因此改用显式启动 Activity，启动成功。该脚本未修改。
- 新建合成 `UI-Demo` 测试账户，检查添加、分组选择、颜色选择与编辑页面。
- 在独立分组管理页重命名测试分组成功；删除该分组后测试账户移至未分组，原账户仍保留。
- 检查浅色/深色首页、添加和备份界面，及操作弹窗、隐私页、应用锁和二维码迁移界面。
- 720×1280 / 320 dpi 的小屏配置下，添加页可滚动至颜色选项及提交按钮；分组输入弹窗在键盘打开时可见。验证后恢复屏幕尺寸及密度。
- 无系统锁屏凭据时，密钥查看被阻止。为该模拟器临时设置测试锁屏凭据后，系统 PIN 认证成功，应用解锁成功，二维码接收配对及连续扫描页面可进入。系统 PIN 页面属于 Android 系统，非应用自定义界面。
- 首次启动隐私页使用新建的隔离 Android 测试用户验证，不清空主用户账户；拒绝后仍停留隐私同意页，同意后进入空账户首页。浅色隐私页状态栏已核对。
- 截图通过模拟器宿主屏幕接口取得，正式应用的 FLAG_SECURE 保持启用。
- 验证结束后移除本次创建的隔离测试用户、临时系统锁屏凭据及 UI-Demo 合成账户，恢复原屏幕尺寸、密度及跟随系统主题；原 UpgradeSentinel 账户仍在。
- 用户在 Android Studio 模拟器查看后确认移除首页底部重复扫码入口；已重新构建、覆盖安装并核对首页及「＋」内保留的扫码入口。

## 验证边界

- 完整 pnpm 门禁仍为“待确认”，没有将依赖阻断算作测试通过。
- 未进行最新真机安装、厂商指纹/面容认证、真实相机二维码识别或 Android↔HarmonyOS 双设备端到端迁移。
- 未在本次测试中导出真实备份、展示/复制真实密钥或恢复密钥，未选择系统分享接收方。备份加密与解析由原安全回归脚本验证，完整备份 UI 流程仍需单独验收。
- 未触发真实异常崩溃或验证所有异常页面状态；空态、表单和主要页面已检查。
- 源码按仓库规则通过 Pull Request 合并至 main，安装包通过 GitHub Release 分发；本次不涉及应用市场发布。
