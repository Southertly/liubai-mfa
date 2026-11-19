import { Feather } from "@expo/vector-icons";
import React from "react";
import { Pressable, StyleSheet, View } from "react-native";

import { LABEL_COLORS } from "@/constants/labelColors";
import { useColors } from "@/hooks/useColors";

export function LabelColorOptions({
  selectedColor,
  onSelect,
}: {
  selectedColor?: string;
  onSelect: (color: string | undefined) => void;
}) {
  const colors = useColors();
  return (
    <View style={styles.options}>
      {[
        { id: undefined, name: "无标签", hex: colors.input },
        ...LABEL_COLORS,
      ].map((option) => {
        const selected = option.id === selectedColor;
        return (
          <Pressable
            key={option.id ?? "none"}
            accessibilityRole="radio"
            accessibilityLabel={option.name}
            accessibilityState={{ selected }}
            onPress={() => onSelect(option.id)}
            style={({ pressed }) => [
              styles.target,
              { opacity: pressed ? 0.7 : 1 },
            ]}
          >
            <View
              style={[
                styles.swatch,
                {
                  backgroundColor: option.hex,
                  borderColor: selected ? colors.foreground : "transparent",
                },
              ]}
            >
              {selected && (
                <Feather
                  name="check"
                  size={18}
                  color={option.id ? "#fff" : colors.foreground}
                />
              )}
              {!selected && !option.id && (
                <Feather
                  name="slash"
                  size={18}
                  color={colors.mutedForeground}
                />
              )}
            </View>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  options: { flexDirection: "row", flexWrap: "wrap", gap: 2 },
  target: {
    width: 48,
    height: 48,
    alignItems: "center",
    justifyContent: "center",
  },
  swatch: {
    width: 38,
    height: 38,
    borderRadius: 19,
    borderWidth: 3,
    alignItems: "center",
    justifyContent: "center",
  },
});
