import { Feather } from "@expo/vector-icons";
import React, { useEffect, useRef, useState } from "react";
import { ActivityIndicator, Modal, Pressable, StyleSheet, Text, View } from "react-native";

import { useLock } from "@/context/LockContext";
import { useColors } from "@/hooks/useColors";

interface SensitiveAuthGateProps {
  visible: boolean;
  reason: string;
  onAuthenticated: () => void;
  onCancel: () => void;
}

export function SensitiveAuthGate({
  visible,
  reason,
  onAuthenticated,
  onCancel,
}: SensitiveAuthGateProps) {
  const colors = useColors();
  const { authenticateSensitiveAction } = useLock();
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState("");
  const attempted = useRef(false);

  const authenticate = async () => {
    if (checking) return;
    setChecking(true);
    setError("");
    const success = await authenticateSensitiveAction(reason);
    setChecking(false);
    if (success) onAuthenticated();
    else setError("身份验证未通过，请重试或使用手机锁屏密码");
  };

  useEffect(() => {
    if (!visible) {
      attempted.current = false;
      setError("");
      return;
    }
    if (attempted.current) return;
    attempted.current = true;
    void authenticate();
  }, [visible]);

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <View style={styles.backdrop}>
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}>
          <View style={[styles.icon, { backgroundColor: colors.primary + "18" }]}>
            <Feather name="shield" size={22} color={colors.primary} />
          </View>
          <Text style={[styles.title, { color: colors.foreground }]}>验证身份</Text>
          <Text style={[styles.reason, { color: colors.mutedForeground }]}>{reason}</Text>

          {checking ? (
            <View style={styles.progress}>
              <ActivityIndicator color={colors.primary} />
              <Text style={[styles.progressText, { color: colors.mutedForeground }]}>
                等待系统验证…
              </Text>
            </View>
          ) : (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="使用系统认证验证身份"
              onPress={authenticate}
              style={({ pressed }) => [
                styles.primaryButton,
                { backgroundColor: colors.primary, opacity: pressed ? 0.75 : 1 },
              ]}
            >
              <Feather name="lock" size={17} color="#fff" />
              <Text style={styles.primaryButtonText}>使用系统认证</Text>
            </Pressable>
          )}

          {!!error && <Text style={[styles.error, { color: colors.destructive }]}>{error}</Text>}

          <Pressable
            accessibilityRole="button"
            accessibilityLabel="取消身份验证"
            onPress={onCancel}
            style={({ pressed }) => [styles.cancelButton, { opacity: pressed ? 0.6 : 1 }]}
          >
            <Text style={[styles.cancelText, { color: colors.mutedForeground }]}>取消</Text>
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
    backgroundColor: "rgba(0,0,0,0.55)",
  },
  card: {
    width: "100%",
    maxWidth: 380,
    borderWidth: 1,
    borderRadius: 20,
    padding: 22,
    alignItems: "center",
    gap: 12,
  },
  icon: { width: 48, height: 48, borderRadius: 24, alignItems: "center", justifyContent: "center" },
  title: { fontSize: 20, fontFamily: "Inter_700Bold" },
  reason: { fontSize: 13, fontFamily: "Inter_400Regular", textAlign: "center", lineHeight: 19 },
  progress: { alignItems: "center", gap: 8, paddingVertical: 12 },
  progressText: { fontSize: 12, fontFamily: "Inter_400Regular" },
  error: { fontSize: 12, fontFamily: "Inter_500Medium", textAlign: "center" },
  primaryButton: {
    width: "100%",
    borderRadius: 12,
    paddingVertical: 13,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
  },
  primaryButtonText: { color: "#fff", fontSize: 14, fontFamily: "Inter_600SemiBold" },
  cancelButton: { paddingHorizontal: 18, paddingVertical: 8 },
  cancelText: { fontSize: 14, fontFamily: "Inter_500Medium" },
});
