import { Feather } from "@expo/vector-icons";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useLock } from "@/context/LockContext";
import { useColors } from "@/hooks/useColors";

export function LockOverlay() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { isLocked, unlockWithSystemAuth } = useLock();
  const [checking, setChecking] = useState(false);
  const attempted = useRef(false);

  const authenticate = useCallback(async () => {
    if (checking) return;
    setChecking(true);
    await unlockWithSystemAuth();
    setChecking(false);
  }, [checking, unlockWithSystemAuth]);

  useEffect(() => {
    if (!isLocked) {
      attempted.current = false;
      return;
    }
    if (attempted.current) return;
    attempted.current = true;
    const frame = requestAnimationFrame(() => {
      void authenticate();
    });
    return () => cancelAnimationFrame(frame);
  }, [authenticate, isLocked]);

  if (!isLocked) return null;

  return (
    <View
      style={[
        styles.overlay,
        {
          backgroundColor: colors.background,
          paddingTop: insets.top + 40,
          paddingBottom: insets.bottom + 24,
        },
      ]}
    >
      <View style={[styles.iconWrap, { backgroundColor: colors.primarySoft }]}>
        <Feather name="lock" size={38} color={colors.primary} />
      </View>
      <Text style={[styles.title, { color: colors.foreground }]}>留白 MFA 已锁定</Text>
      <Text style={[styles.subtitle, { color: colors.mutedForeground }]}>
        使用指纹、面容或手机锁屏密码解锁
      </Text>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="使用系统认证解锁"
        onPress={authenticate}
        disabled={checking}
        style={({ pressed }) => [
          styles.authButton,
          {
            backgroundColor: colors.primary,
            opacity: checking ? 0.65 : pressed ? 0.8 : 1,
          },
        ]}
      >
        {checking ? (
          <ActivityIndicator color="#fff" />
        ) : (
          <Feather name="lock" size={18} color="#fff" />
        )}
        <Text style={styles.authButtonText}>
          验证身份
        </Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 9999,
    alignItems: "center",
    justifyContent: "center",
    gap: 16,
    paddingHorizontal: 28,
  },
  iconWrap: {
    width: 84,
    height: 84,
    borderRadius: 42,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 4,
  },
  title: { fontSize: 24, fontFamily: "Inter_700Bold" },
  subtitle: {
    fontSize: 13,
    fontFamily: "Inter_400Regular",
    textAlign: "center",
    lineHeight: 21,
    marginBottom: 12,
  },
  authButton: {
    width: 220,
    minHeight: 48,
    borderRadius: 8,
    paddingVertical: 15,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
  },
  authButtonText: { color: "#fff", fontSize: 15, fontFamily: "Inter_600SemiBold" },
});
