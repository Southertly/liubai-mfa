#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "用法：$0 <旧版本 APK> <新版本 APK>" >&2
  exit 2
fi

old_apk="$1"
new_apk="$2"
[[ -f "${old_apk}" && -f "${new_apk}" ]] || { echo "APK 文件不存在" >&2; exit 2; }

android_sdk="${ANDROID_HOME:-${HOME}/Library/Android/sdk}"
apksigner="$(find "${android_sdk}/build-tools" -type f -name apksigner 2>/dev/null | sort | tail -1)"
aapt2="$(find "${android_sdk}/build-tools" -type f -name aapt2 2>/dev/null | sort | tail -1)"
adb_bin="${android_sdk}/platform-tools/adb"
[[ -x "${apksigner}" && -x "${aapt2}" && -x "${adb_bin}" ]] || { echo "缺少 Android SDK 签名或设备工具" >&2; exit 1; }

old_cert="$(${apksigner} verify --print-certs "${old_apk}" | sed -n 's/^Signer #1 certificate SHA-256 digest: //p' | head -1)"
new_cert="$(${apksigner} verify --print-certs "${new_apk}" | sed -n 's/^Signer #1 certificate SHA-256 digest: //p' | head -1)"
[[ -n "${old_cert}" && "${old_cert}" == "${new_cert}" ]] || { echo "签名不一致，已停止；不会卸载或清除设备数据" >&2; exit 1; }

old_version="$(${aapt2} dump badging "${old_apk}" | sed -n "s/.*versionCode='\([^']*\)'.*/\1/p" | head -1)"
new_version="$(${aapt2} dump badging "${new_apk}" | sed -n "s/.*versionCode='\([^']*\)'.*/\1/p" | head -1)"
[[ "${new_version}" -gt "${old_version}" ]] || { echo "新版本号必须大于旧版本号" >&2; exit 1; }

"${adb_bin}" get-state >/dev/null
"${adb_bin}" install -r "${new_apk}"
"${adb_bin}" shell monkey -p com.mfaverifier.app -c android.intent.category.LAUNCHER 1 >/dev/null
echo "覆盖安装完成。脚本未读取、导出或清除账户；请在设备上核对原账户和验证码。"
