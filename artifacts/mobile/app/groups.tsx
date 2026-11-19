import React, { useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { ActionDialog } from "@/components/ActionDialog";
import { PageHeader } from "@/components/PageHeader";
import { useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";

export default function GroupsScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { accounts, groups, renameGroup, removeGroup } = useAccounts();
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [deleting, setDeleting] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const save = async (group: string) => {
    if (busy) return;
    if (!name.trim()) {
      setError("请输入分组名称");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await renameGroup(group, name.trim());
      setEditing(null);
    } catch {
      setError("保存失败，请重试");
    } finally {
      setBusy(false);
    }
  };

  const remove = async (group: string) => {
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      await removeGroup(group);
    } catch {
      setError("删除分组失败，请重试");
    } finally {
      setBusy(false);
    }
  };

  return (
    <KeyboardAvoidingView
      style={[styles.screen, { backgroundColor: colors.background }]}
      behavior={Platform.OS === "ios" ? "padding" : "height"}
    >
      <PageHeader title="管理分组" />
      <ScrollView
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={[
          styles.content,
          { paddingBottom: insets.bottom + 24 },
        ]}
      >
        {!!error && (
          <Text accessibilityRole="alert" style={{ color: colors.destructive }}>
            {error}
          </Text>
        )}
        {groups.length === 0 && (
          <Text style={[styles.empty, { color: colors.mutedForeground }]}>
            暂无分组
          </Text>
        )}
        {groups.map((group) => (
          <View
            key={group}
            style={[
              styles.card,
              { backgroundColor: colors.card, borderColor: colors.border },
            ]}
          >
            <View style={styles.row}>
              <Text style={[styles.name, { color: colors.foreground }]}>
                {group}
              </Text>
              <Text style={[styles.count, { color: colors.mutedForeground }]}>
                {accounts.filter((account) => account.group === group).length}{" "}
                个账户
              </Text>
            </View>
            {editing === group ? (
              <>
                <TextInput
                  accessibilityLabel="新的分组名称"
                  value={name}
                  onChangeText={(value) => {
                    setName(value);
                    setError("");
                  }}
                  editable={!busy}
                  autoFocus
                  selectTextOnFocus
                  placeholder="输入分组名称"
                  placeholderTextColor={colors.mutedForeground}
                  returnKeyType="done"
                  onSubmitEditing={() => void save(group)}
                  style={[
                    styles.input,
                    { backgroundColor: colors.input, color: colors.foreground },
                  ]}
                />
                <View style={styles.row}>
                  <Pressable
                    disabled={busy}
                    onPress={() => {
                      setEditing(null);
                      setError("");
                    }}
                    style={[styles.button, { backgroundColor: colors.input }]}
                  >
                    <Text style={{ color: colors.foreground }}>取消</Text>
                  </Pressable>
                  <Pressable
                    disabled={busy || !name.trim()}
                    onPress={() => void save(group)}
                    style={[
                      styles.button,
                      {
                        backgroundColor: colors.primary,
                        opacity: busy || !name.trim() ? 0.5 : 1,
                      },
                    ]}
                  >
                    <Text style={{ color: "#fff" }}>保存</Text>
                  </Pressable>
                </View>
              </>
            ) : (
              <View style={styles.row}>
                <Pressable
                  disabled={busy}
                  onPress={() => {
                    setEditing(group);
                    setName(group);
                    setError("");
                  }}
                  style={[styles.button, { backgroundColor: colors.input }]}
                >
                  <Text style={{ color: colors.foreground }}>重命名</Text>
                </Pressable>
                <Pressable
                  disabled={busy}
                  onPress={() => setDeleting(group)}
                  style={[
                    styles.button,
                    { borderColor: colors.destructive, borderWidth: 1 },
                  ]}
                >
                  <Text style={{ color: colors.destructive }}>删除分组</Text>
                </Pressable>
              </View>
            )}
          </View>
        ))}
      </ScrollView>
      <ActionDialog
        visible={deleting !== null}
        title="删除分组"
        message={`删除「${deleting ?? ""}」后，组内账户将移至未分组，账户不会被删除。`}
        onClose={() => setDeleting(null)}
        actions={[
          {
            label: "删除分组",
            destructive: true,
            onPress: () => {
              if (deleting !== null) void remove(deleting);
            },
          },
        ]}
      />
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  content: { paddingHorizontal: 16, paddingTop: 8, gap: 12 },
  card: { padding: 14, borderWidth: 1, borderRadius: 8, gap: 10 },
  row: { flexDirection: "row", alignItems: "center", gap: 8 },
  name: { flex: 1, fontSize: 16, fontFamily: "Inter_500Medium" },
  count: { fontSize: 11, fontFamily: "Inter_400Regular" },
  input: {
    minHeight: 48,
    borderRadius: 8,
    paddingHorizontal: 12,
    fontSize: 15,
  },
  button: {
    flex: 1,
    minHeight: 48,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center",
    padding: 8,
  },
  empty: { textAlign: "center", paddingTop: 60, fontSize: 15 },
});
