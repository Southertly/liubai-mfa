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
    const timer = setTimeout(authenticate, 400);
    return () => clearTimeout(timer);
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
      <View style={[styles.iconWrap, { backgroundColor: colors.primary + "18" }]}>
        <Feather name="shield" size={36} color={colors.primary} />
      </View>
      <Text style={[styles.title, { color: colors.foreground }]}>验证身份</Text>
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
          {checking ? "等待系统验证…" : "使用系统认证"}
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
    width: 72,
    height: 72,
    borderRadius: 20,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 4,
  },
  title: { fontSize: 22, fontFamily: "Inter_700Bold" },
  subtitle: {
    fontSize: 14,
    fontFamily: "Inter_400Regular",
    textAlign: "center",
    lineHeight: 21,
    marginBottom: 12,
  },
  authButton: {
    width: "100%",
    maxWidth: 340,
    borderRadius: 14,
    paddingVertical: 15,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
  },
  authButtonText: { color: "#fff", fontSize: 15, fontFamily: "Inter_600SemiBold" },
});
