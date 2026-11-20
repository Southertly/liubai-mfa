# Windows 版本验证记录

## Electron 正式版 1.0.3

用独立产品标识和临时目录验证从 1.0.2 覆盖升级。升级后桌面和开始菜单快捷方式指向新版本的独立图标资源，Windows 读取到的图标与蓝色盾牌源图一致。测试安装已卸载。[Windows 1.0.3 发布页](https://github.com/Southertly/liubai-mfa/releases/tag/windows-v1.0.3)

## Electron 正式版 1.0.1

在 Windows 11 x64 上通过临时安装验证。测试副本使用独立的产品标识和含中文的自定义目录，验证了桌面和开始菜单快捷方式、EXE 内嵌图标及「已安装的应用」列表图标。卸载测试副本后核对快捷方式已移除。[Windows 1.0.1 发布页](https://github.com/Southertly/liubai-mfa/releases/tag/windows-v1.0.1)

## Electron 正式版 1.0.0

验证日期为 2026-11-20，系统为 Windows 11 x64。发布的是未签名 MSI。[Windows 1.0.0 发布页](https://github.com/Southertly/liubai-mfa/releases/tag/windows-v1.0.0)

- 使用临时账户库验证含中文的自定义安装目录、启动与重启，并核对卸载 MSI 后账户库文件完整。
- 正式安装后验证预览版账户库迁入、重启后账户可见、验证码继续刷新。卸载预览版后再次校验正式版账户库和保留的备份副本。
- 核对正式安装包的远端 SHA-256 与本地产物一致，并确认正式版检查更新能识别已发布的 1.0.0。
