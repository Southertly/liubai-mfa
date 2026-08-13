import { Feather } from "@expo/vector-icons";
import * as Haptics from "expo-haptics";
import React from "react";
import {
  Modal,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { LABEL_COLORS } from "@/constants/labelColors";
import { useColors } from "@/hooks/useColors";

interface Props {
  visible: boolean;
  selectedColor?: string;
  onSelect: (colorId: string | undefined) => void;
  onClose: () => void;
}

export function ColorPickerModal({ visible, selectedColor, onSelect, onClose }: Props) {
  const colors = useColors();

  const handleSelect = (id: string | undefined) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    onSelect(id);
    onClose();
  };

  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={onClose}
    >
      <Pressable style={styles.backdrop} onPress={onClose}>
        <Pressable
          style={[styles.sheet, { backgroundColor: colors.card, borderColor: colors.border }]}
          onPress={(e) => e.stopPropagation()}
        >
          <View style={styles.handle} />

          <Text style={[styles.title, { color: colors.foreground }]}>
            选择标签颜色
          </Text>

          <View style={styles.grid}>
            {/* No color option */}
            <Pressable
              onPress={() => handleSelect(undefined)}
              style={[
                styles.swatch,
                styles.noColor,
                {
                  borderColor: !selectedColor ? colors.foreground : colors.border,
                  backgroundColor: colors.muted,
                },
              ]}
            >
              {!selectedColor && (
                <Feather name="check" size={14} color={colors.foreground} />
              )}
              {!(!selectedColor) && (
                <Feather name="slash" size={14} color={colors.mutedForeground} />
              )}
            </Pressable>

            {LABEL_COLORS.map((lc) => {
              const active = selectedColor === lc.id;
              return (
                <Pressable
                  key={lc.id}
                  onPress={() => handleSelect(lc.id)}
                  style={[
                    styles.swatch,
                    {
                      backgroundColor: lc.hex,
                      borderColor: active ? colors.foreground : "transparent",
                      borderWidth: active ? 2.5 : 2,
                    },
                  ]}
                >
                  {active && (
                    <Feather name="check" size={14} color="#fff" />
                  )}
                </Pressable>
              );
            })}
          </View>

          {/* Color name label */}
          <Text style={[styles.colorName, { color: colors.mutedForeground }]}>
            {selectedColor
              ? LABEL_COLORS.find((c) => c.id === selectedColor)?.name ?? ""
              : "无标签"}
          </Text>

          <Pressable
            onPress={onClose}
            style={[styles.cancelBtn, { borderColor: colors.border }]}
          >
            <Text style={[styles.cancelText, { color: colors.mutedForeground }]}>
              取消
            </Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const SWATCH = 44;

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: "rgba(0,0,0,0.45)",
    justifyContent: "flex-end",
  },
  sheet: {
    borderTopLeftRadius: 8,
    borderTopRightRadius: 8,
    borderWidth: 1,
    borderBottomWidth: 0,
    paddingHorizontal: 24,
    paddingBottom: 32,
    paddingTop: 12,
    alignItems: "center",
    gap: 16,
  },
  handle: {
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: "#8888",
    marginBottom: 4,
  },
  title: {
    fontSize: 16,
    fontFamily: "Inter_600SemiBold",
  },
  grid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "center",
  },
  swatch: {
    width: SWATCH,
    height: SWATCH,
    borderRadius: SWATCH / 2,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 2,
  },
  noColor: {
    borderWidth: 1.5,
  },
  colorName: {
    fontSize: 13,
    fontFamily: "Inter_500Medium",
    height: 18,
  },
  cancelBtn: {
    width: "100%",
    borderWidth: 1,
    borderRadius: 8,
    paddingVertical: 13,
    alignItems: "center",
  },
  cancelText: {
    fontSize: 15,
    fontFamily: "Inter_500Medium",
  },
});
