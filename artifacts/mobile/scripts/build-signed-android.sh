#!/usr/bin/env bash
set -euo pipefail

signing_dir="${MFA_SIGNING_DIR:-${HOME}/Library/Application Support/LiubaiMFA/signing}"
keystore_path="${MFA_ANDROID_KEYSTORE_PATH:-${signing_dir}/liubai-mfa-release.p12}"
key_alias="${MFA_ANDROID_KEY_ALIAS:-liubai-mfa-release}"
keychain_service="${MFA_ANDROID_KEYCHAIN_SERVICE:-com.mfaverifier.app.android-signing}"

if [[ ! -f "${keystore_path}" ]]; then
  echo "未找到正式签名文件：${keystore_path}" >&2
  exit 1
fi

if [[ -z "${MFA_ANDROID_KEYSTORE_PASSWORD:-}" ]]; then
  MFA_ANDROID_KEYSTORE_PASSWORD="$(security find-generic-password -a "${key_alias}" -s "${keychain_service}" -w)"
fi
if [[ -z "${MFA_ANDROID_KEY_PASSWORD:-}" ]]; then
  MFA_ANDROID_KEY_PASSWORD="${MFA_ANDROID_KEYSTORE_PASSWORD}"
fi

export MFA_ANDROID_KEYSTORE_PATH="${keystore_path}"
export MFA_ANDROID_KEYSTORE_PASSWORD
export MFA_ANDROID_KEY_ALIAS="${key_alias}"
export MFA_ANDROID_KEY_PASSWORD

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${script_dir}/../android"
./gradlew app:assembleRelease
