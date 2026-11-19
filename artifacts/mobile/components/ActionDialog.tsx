import React from "react";
import {
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useColors } from "@/hooks/useColors";

export function ActionDialog({
  visible,
  title,
  message,
  actions,
  onClose,
}: {
  visible: boolean;
  title: string;
  message?: string;
  actions: { label: string; onPress: () => void; destructive?: boolean }[];
  onClose: () => void;
}) {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={onClose}
    >
      <View
        style={[
          styles.overlay,
          {
            backgroundColor: colors.scrim,
            paddingTop: insets.top + 20,
            paddingBottom: insets.bottom + 20,
          },
        ]}
      >
        <Pressable
          accessibilityLabel="关闭对话框"
          accessibilityRole="button"
          style={StyleSheet.absoluteFill}
          onPress={onClose}
        />
        <View
          accessibilityViewIsModal
          style={[styles.card, { backgroundColor: colors.surfaceRaised }]}
        >
          <ScrollView contentContainerStyle={styles.content}>
            <Text
              accessibilityRole="header"
              style={[styles.title, { color: colors.foreground }]}
            >
              {title}
            </Text>
            {!!message && (
              <Text style={[styles.message, { color: colors.mutedForeground }]}>
                {message}
              </Text>
            )}
            {actions.map((action) => (
              <Pressable
                key={action.label}
                accessibilityRole="button"
                onPress={() => {
                  onClose();
                  action.onPress();
                }}
                style={({ pressed }) => [
                  styles.button,
                  { backgroundColor: colors.input, opacity: pressed ? 0.7 : 1 },
                ]}
              >
                <Text
                  style={[
                    styles.buttonText,
                    {
                      color: action.destructive
                        ? colors.destructive
                        : colors.foreground,
                    },
                  ]}
                >
                  {action.label}
                </Text>
              </Pressable>
            ))}
            <Pressable
              accessibilityRole="button"
              onPress={onClose}
              style={styles.button}
            >
              <Text
                style={[styles.buttonText, { color: colors.mutedForeground }]}
              >
                取消
              </Text>
            </Pressable>
          </ScrollView>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 24,
  },
  card: { width: "100%", maxWidth: 420, maxHeight: "100%", borderRadius: 8 },
  content: { padding: 20, gap: 12 },
  title: { fontSize: 20, fontFamily: "Inter_700Bold" },
  message: { fontSize: 13, lineHeight: 20, fontFamily: "Inter_400Regular" },
  button: {
    minHeight: 48,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    padding: 12,
  },
  buttonText: { fontSize: 14, fontFamily: "Inter_500Medium" },
});
