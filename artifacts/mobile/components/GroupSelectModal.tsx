import { Feather } from "@expo/vector-icons";
import React, { useState } from "react";
import {
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useColors } from "@/hooks/useColors";

interface Props {
  visible: boolean;
  groups: string[];
  selectedGroup?: string;
  onSelect: (group: string | undefined) => void;
  onClose: () => void;
}

export function GroupSelectModal({
  visible,
  groups,
  selectedGroup,
  onSelect,
  onClose,
}: Props) {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const [newGroupName, setNewGroupName] = useState("");
  const [showInput, setShowInput] = useState(false);

  const handleClose = () => {
    setNewGroupName("");
    setShowInput(false);
    onClose();
  };
  const select = (group: string | undefined) => {
    onSelect(group);
    handleClose();
  };
  const handleNewGroup = () => {
    if (newGroupName.trim()) select(newGroupName.trim());
  };

  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={handleClose}
    >
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
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
          onPress={handleClose}
          accessibilityRole="button"
          accessibilityLabel="关闭分组选择"
        />
        <View style={[styles.card, { backgroundColor: colors.surfaceRaised }]}>
          <ScrollView
            keyboardShouldPersistTaps="handled"
            contentContainerStyle={styles.content}
          >
            <Text style={[styles.title, { color: colors.foreground }]}>
              选择分组
            </Text>
            {[undefined, ...groups].map((group) => (
              <Pressable
                key={group ?? "ungrouped"}
                accessibilityRole="radio"
                accessibilityState={{ selected: selectedGroup === group }}
                onPress={() => select(group)}
                style={({ pressed }) => [
                  styles.option,
                  { backgroundColor: colors.input, opacity: pressed ? 0.7 : 1 },
                ]}
              >
                <Text style={[styles.optionText, { color: colors.foreground }]}>
                  {group ?? "未分组"}
                </Text>
                {selectedGroup === group && (
                  <Feather name="check" size={18} color={colors.primary} />
                )}
              </Pressable>
            ))}
            {showInput ? (
              <>
                <TextInput
                  accessibilityLabel="新分组名称"
                  value={newGroupName}
                  onChangeText={setNewGroupName}
                  autoFocus
                  placeholder="输入新分组名称"
                  placeholderTextColor={colors.mutedForeground}
                  returnKeyType="done"
                  onSubmitEditing={handleNewGroup}
                  style={[
                    styles.input,
                    { backgroundColor: colors.input, color: colors.foreground },
                  ]}
                />
                <Pressable
                  disabled={!newGroupName.trim()}
                  onPress={handleNewGroup}
                  style={[
                    styles.option,
                    styles.center,
                    {
                      backgroundColor: colors.primary,
                      opacity: newGroupName.trim() ? 1 : 0.5,
                    },
                  ]}
                >
                  <Text style={{ color: "#fff" }}>创建并选择</Text>
                </Pressable>
              </>
            ) : (
              <Pressable
                onPress={() => setShowInput(true)}
                style={[
                  styles.option,
                  styles.center,
                  { backgroundColor: colors.primarySoft },
                ]}
              >
                <Text style={{ color: colors.primary }}>新建分组</Text>
              </Pressable>
            )}
            <Pressable
              onPress={handleClose}
              style={[styles.option, styles.center]}
            >
              <Text style={{ color: colors.mutedForeground }}>取消</Text>
            </Pressable>
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    paddingHorizontal: 24,
  },
  card: { width: "100%", maxWidth: 420, maxHeight: "100%", borderRadius: 8 },
  content: { padding: 20, gap: 12 },
  title: { fontSize: 20, fontFamily: "Inter_700Bold" },
  option: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    padding: 12,
    borderRadius: 8,
  },
  center: { justifyContent: "center" },
  optionText: { flex: 1, fontSize: 14, fontFamily: "Inter_500Medium" },
  input: {
    minHeight: 48,
    borderRadius: 8,
    paddingHorizontal: 12,
    fontSize: 15,
  },
});
