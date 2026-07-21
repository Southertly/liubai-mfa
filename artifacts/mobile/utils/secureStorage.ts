import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";

import { generateRecoveryKey } from "@/utils/secureCrypto";

const DEVICE_KEY_NAME = "mfa_device_encryption_key_v1";

async function readDeviceKey(): Promise<string | null> {
  if (Platform.OS === "web") {
    return globalThis.localStorage?.getItem(DEVICE_KEY_NAME) ?? null;
  }
  return SecureStore.getItemAsync(DEVICE_KEY_NAME);
}

async function writeDeviceKey(key: string): Promise<void> {
  if (Platform.OS === "web") {
    globalThis.localStorage?.setItem(DEVICE_KEY_NAME, key);
    return;
  }
  await SecureStore.setItemAsync(DEVICE_KEY_NAME, key, {
    keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
  });
}

export async function getOrCreateDeviceEncryptionKey(): Promise<string> {
  const existing = await readDeviceKey();
  if (existing) return existing;
  const created = generateRecoveryKey();
  await writeDeviceKey(created);
  return created;
}

