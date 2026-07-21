import { Feather } from "@expo/vector-icons";
import React, { useState } from "react";
import {
  FlatList,
  Modal,
  Platform,
  Pressable,
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

  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  const handleNewGroup = () => {
    const name = newGroupName.trim();
    if (!name) return;
    onSelect(name);
    setNewGroupName("");
    setShowInput(false);
  };

  const handleClose = () => {
    setNewGroupName("");
    setShowInput(false);
    onClose();
  };

  const options = groups.filter((g) => g !== selectedGroup);

  return (
    <Modal
      visible={visible}
      transparent
      animationType="slide"
      onRequestClose={handleClose}
    >
      <Pressable style={styles.backdrop} onPress={handleClose} />
      <View
        style={[
          styles.sheet,
          {
            backgroundColor: colors.card,
            paddingBottom: bottomPad + 16,
          },
        ]}
      >
        <View
          style={[styles.handle, { backgroundColor: colors.mutedForeground }]}
        />
        <Text style={[styles.title, { color: colors.foreground }]}>
          移动到分组
        </Text>

        {selectedGroup && (
          <Pressable
            onPress={() => onSelect(undefined)}
            style={[
              styles.option,
              { borderBottomColor: colors.border },
            ]}
          >
            <Feather name="x-circle" size={18} color={colors.mutedForeground} />
            <Text
              style={[styles.optionText, { color: colors.mutedForeground }]}
            >
              移出分组
            </Text>
          </Pressable>
        )}

        {options.map((group) => (
          <Pressable
            key={group}
            onPress={() => onSelect(group)}
            style={({ pressed }) => [
              styles.option,
              {
                borderBottomColor: colors.border,
                backgroundColor: pressed
                  ? colors.primary + "12"
                  : "transparent",
              },
            ]}
          >
            <Feather name="folder" size={18} color={colors.primary} />
            <Text style={[styles.optionText, { color: colors.foreground }]}>
              {group}
            </Text>
          </Pressable>
        ))}

        {showInput ? (
          <View
            style={[styles.newGroupRow, { borderTopColor: colors.border }]}
          >
            <TextInput
              style={[
                styles.newGroupInput,
                {
                  backgroundColor: colors.background,
                  borderColor: colors.border,
                  color: colors.foreground,
                },
              ]}
              value={newGroupName}
              onChangeText={setNewGroupName}
              placeholder="输入分组名称"
              placeholderTextColor={colors.mutedForeground}
              autoFocus
              returnKeyType="done"
              onSubmitEditing={handleNewGroup}
            />
            <Pressable
              onPress={handleNewGroup}
              style={[
                styles.newGroupBtn,
                {
                  backgroundColor:
                    newGroupName.trim() ? colors.primary : colors.muted,
                },
              ]}
            >
              <Feather name="check" size={18} color="#fff" />
            </Pressable>
          </View>
        ) : (
          <Pressable
            onPress={() => setShowInput(true)}
            style={({ pressed }) => [
              styles.option,
              {
                borderBottomColor: colors.border,
                backgroundColor: pressed
                  ? colors.primary + "12"
                  : "transparent",
              },
            ]}
          >
            <Feather name="plus-circle" size={18} color={colors.accent} />
            <Text style={[styles.optionText, { color: colors.accent }]}>
              新建分组…
            </Text>
          </Pressable>
        )}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: "rgba(0,0,0,0.45)",
  },
  sheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    paddingTop: 12,
    paddingHorizontal: 16,
    minHeight: 200,
  },
  handle: {
    width: 36,
    height: 4,
    borderRadius: 2,
    alignSelf: "center",
    marginBottom: 16,
    opacity: 0.3,
  },
  title: {
    fontSize: 16,
    fontFamily: "Inter_600SemiBold",
    marginBottom: 12,
    paddingHorizontal: 4,
  },
  option: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    paddingVertical: 14,
    paddingHorizontal: 4,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderRadius: 8,
  },
  optionText: {
    fontSize: 15,
    fontFamily: "Inter_500Medium",
  },
  newGroupRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingTop: 14,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  newGroupInput: {
    flex: 1,
    borderWidth: 1,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: 15,
    fontFamily: "Inter_400Regular",
  },
  newGroupBtn: {
    width: 40,
    height: 40,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center",
  },
});
