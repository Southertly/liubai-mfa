import AsyncStorage from "@react-native-async-storage/async-storage";
import * as LocalAuthentication from "expo-local-authentication";
import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import { AppState, Platform } from "react-native";

const LEGACY_PIN_KEYS = ["lock_pin_hash", "lock_pin_length", "lock_enabled"];
const BG_TIMEOUT = 60_000;

interface LockContextValue {
  isLocked: boolean;
  systemAuthAvailable: boolean;
  lock: () => void;
  unlockWithSystemAuth: () => Promise<boolean>;
  authenticateSensitiveAction: (reason: string) => Promise<boolean>;
}

const LockContext = createContext<LockContextValue | null>(null);

export function LockProvider({ children }: { children: React.ReactNode }) {
  const isNative = Platform.OS !== "web";
  const [isLocked, setIsLocked] = useState(isNative);
  const [systemAuthAvailable, setSystemAuthAvailable] = useState(false);
  const bgTimestamp = useRef<number | null>(null);

  useEffect(() => {
    (async () => {
      await AsyncStorage.multiRemove(LEGACY_PIN_KEYS);
      if (!isNative) return;
      const level = await LocalAuthentication.getEnrolledLevelAsync();
      setSystemAuthAvailable(level !== LocalAuthentication.SecurityLevel.NONE);
    })();
  }, [isNative]);

  useEffect(() => {
    if (!isNative) return;
    const sub = AppState.addEventListener("change", (state) => {
      if (state === "background" || state === "inactive") {
        bgTimestamp.current = Date.now();
      } else if (state === "active") {
        if (
          bgTimestamp.current !== null &&
          Date.now() - bgTimestamp.current >= BG_TIMEOUT
        ) {
          setIsLocked(true);
        }
        bgTimestamp.current = null;
      }
    });
    return () => sub.remove();
  }, [isNative]);

  const authenticate = useCallback(async (reason: string): Promise<boolean> => {
    if (!isNative) return true;
    try {
      const result = await LocalAuthentication.authenticateAsync({
        promptMessage: reason,
        fallbackLabel: "使用手机锁屏密码",
        cancelLabel: "取消",
        disableDeviceFallback: false,
      });
      return result.success;
    } catch {
      return false;
    }
  }, [isNative]);

  const lock = useCallback(() => {
    if (isNative) setIsLocked(true);
  }, [isNative]);

  const unlockWithSystemAuth = useCallback(async (): Promise<boolean> => {
    const success = await authenticate("验证身份以解锁");
    if (success) setIsLocked(false);
    return success;
  }, [authenticate]);

  const authenticateSensitiveAction = useCallback(
    (reason: string) => authenticate(reason),
    [authenticate],
  );

  return (
    <LockContext.Provider
      value={{
        isLocked,
        systemAuthAvailable,
        lock,
        unlockWithSystemAuth,
        authenticateSensitiveAction,
      }}
    >
      {children}
    </LockContext.Provider>
  );
}

export function useLock(): LockContextValue {
  const ctx = useContext(LockContext);
  if (!ctx) throw new Error("useLock must be used within LockProvider");
  return ctx;
}
