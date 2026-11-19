import { Feather } from "@expo/vector-icons";
import { router } from "expo-router";
import React from "react";
import { Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useColors } from "@/hooks/useColors";

export function PageHeader({ title }: { title: string }) {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  return (
    <View
      style={[
        styles.header,
        {
          paddingTop: (Platform.OS === "web" ? 32 : insets.top) + 12,
          backgroundColor: colors.background,
        },
      ]}
    >
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="返回"
        onPress={() => router.back()}
        style={({ pressed }) => [
          styles.back,
          { backgroundColor: colors.input, opacity: pressed ? 0.7 : 1 },
        ]}
      >
        <Feather name="chevron-left" size={28} color={colors.foreground} />
      </Pressable>
      <Text
        accessibilityRole="header"
        style={[styles.title, { color: colors.foreground }]}
      >
        {title}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingHorizontal: 16,
    paddingBottom: 12,
  },
  back: {
    width: 48,
    height: 48,
    borderRadius: 24,
    alignItems: "center",
    justifyContent: "center",
  },
  title: { flex: 1, fontSize: 25, fontFamily: "Inter_700Bold" },
});
