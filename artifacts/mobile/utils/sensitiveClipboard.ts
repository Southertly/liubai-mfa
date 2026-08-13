import * as Clipboard from "expo-clipboard";
import { NativeModules, Platform } from "react-native";

interface SensitiveClipboardNativeModule {
  setSensitiveText(value: string, ttlMs: number): Promise<boolean>;
  clearSensitiveText(): Promise<boolean>;
}

const nativeModule = NativeModules.SensitiveClipboard as SensitiveClipboardNativeModule | undefined;

export async function setSensitiveClipboard(value: string, ttlMs: number): Promise<void> {
  if (Platform.OS === "android" && nativeModule) {
    await nativeModule.setSensitiveText(value, ttlMs);
    return;
  }
  await Clipboard.setStringAsync(value);
}

export async function clearSensitiveClipboard(): Promise<void> {
  if (Platform.OS === "android" && nativeModule) {
    await nativeModule.clearSensitiveText();
  }
}
