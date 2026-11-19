import * as Haptics from "expo-haptics";
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

import { LabelColorOptions } from "@/components/LabelColorOptions";
import { useColors } from "@/hooks/useColors";

interface Props {
  visible: boolean;
  selectedColor?: string;
  onSelect: (colorId: string | undefined) => void;
  onClose: () => void;
}

export function ColorPickerModal({
  visible,
  selectedColor,
  onSelect,
  onClose,
}: Props) {
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
          style={StyleSheet.absoluteFill}
          onPress={onClose}
          accessibilityRole="button"
          accessibilityLabel="关闭颜色选择"
        />
        <View style={[styles.card, { backgroundColor: colors.surfaceRaised }]}>
          <ScrollView contentContainerStyle={styles.content}>
            <Text style={[styles.title, { color: colors.foreground }]}>
              更改标签颜色
            </Text>
            <LabelColorOptions
              selectedColor={selectedColor}
              onSelect={(color) => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onSelect(color);
                onClose();
              }}
            />
            <Pressable
              onPress={onClose}
              style={styles.cancel}
              accessibilityRole="button"
            >
              <Text style={{ color: colors.mutedForeground }}>取消</Text>
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
  cancel: { minHeight: 48, alignItems: "center", justifyContent: "center" },
});
