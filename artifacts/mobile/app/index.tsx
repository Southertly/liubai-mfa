import { Feather } from "@expo/vector-icons";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
import React, { useCallback, useMemo, useRef, useState } from "react";
import {
  Alert,
  FlatList,
  LayoutAnimation,
  Modal,
  Platform,
  Pressable,
  SectionList,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { AccountCard } from "@/components/AccountCard";
import { GroupSelectModal } from "@/components/GroupSelectModal";
import { Account, useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";

interface Section {
  title: string;
  data: Account[];
  totalCount: number;
  isCollapsed: boolean;
}

export default function HomeScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { accounts, groups, removeAccount, updateAccount, renameGroup, removeGroup, loading } =
    useAccounts();

  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set());
  const [movingAccount, setMovingAccount] = useState<Account | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const searchInputRef = useRef<TextInput>(null);

  // Rename modal state
  const [renameTarget, setRenameTarget] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");

  const topPad = Platform.OS === "web" ? 67 : insets.top;
  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  const toggleGroup = useCallback((name: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setExpandedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }, []);

  const handleGroupLongPress = useCallback(
    (name: string) => {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      Alert.alert(`分组：${name}`, undefined, [
        {
          text: "重命名",
          onPress: () => {
            setRenameTarget(name);
            setRenameValue(name);
          },
        },
        {
          text: "删除分组",
          style: "destructive",
          onPress: () => {
            Alert.alert(
              "删除分组",
              `删除「${name}」分组后，组内账户将移至未分组。`,
              [
                { text: "取消", style: "cancel" },
                {
                  text: "删除",
                  style: "destructive",
                  onPress: () => removeGroup(name),
                },
              ]
            );
          },
        },
        { text: "取消", style: "cancel" },
      ]);
    },
    [removeGroup]
  );

  const handleRenameConfirm = useCallback(async () => {
    const newName = renameValue.trim();
    if (!newName || !renameTarget || newName === renameTarget) {
      setRenameTarget(null);
      return;
    }
    await renameGroup(renameTarget, newName);
    // Keep expanded state consistent
    setExpandedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(renameTarget)) {
        next.delete(renameTarget);
        next.add(newName);
      }
      return next;
    });
    setRenameTarget(null);
  }, [renameValue, renameTarget, renameGroup]);

  const filteredAccounts = useMemo(() => {
    if (!searchQuery.trim()) return [];
    const q = searchQuery.trim().toLowerCase();
    return accounts.filter(
      (a) =>
        a.issuer.toLowerCase().includes(q) ||
        a.account.toLowerCase().includes(q) ||
        (a.group ?? "").toLowerCase().includes(q)
    );
  }, [accounts, searchQuery]);

  const sections: Section[] = useMemo(() => {
    const grouped = new Map<string, Account[]>();
    const ungrouped: Account[] = [];

    for (const acc of accounts) {
      if (acc.group) {
        const arr = grouped.get(acc.group) ?? [];
        arr.push(acc);
        grouped.set(acc.group, arr);
      } else {
        ungrouped.push(acc);
      }
    }

    const result: Section[] = [...grouped.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([title, allData]) => {
        const isCollapsed = !expandedGroups.has(title);
        return {
          title,
          data: isCollapsed ? [] : allData,
          totalCount: allData.length,
          isCollapsed,
        };
      });

    if (ungrouped.length > 0) {
      result.push({ title: "", data: ungrouped, totalCount: ungrouped.length, isCollapsed: false });
    }

    return result;
  }, [accounts, expandedGroups]);

  const handleMoveToGroup = async (group: string | undefined) => {
    if (!movingAccount) return;
    await updateAccount(movingAccount.id, { group });
    setMovingAccount(null);
  };

  const clearSearch = () => {
    setSearchQuery("");
    searchInputRef.current?.blur();
  };

  const isSearching = searchQuery.trim().length > 0;

  const renderSectionHeader = useCallback(
    ({ section }: { section: Section }) => {
      if (!section.title) return null;
      const isExpanded = !section.isCollapsed;
      return (
        <Pressable
          onPress={() => toggleGroup(section.title)}
          onLongPress={() => handleGroupLongPress(section.title)}
          delayLongPress={400}
          style={({ pressed }) => [
            styles.sectionHeader,
            {
              backgroundColor: colors.background,
              opacity: pressed ? 0.75 : 1,
            },
          ]}
        >
          <View
            style={[styles.sectionIconWrap, { backgroundColor: colors.primary + "18" }]}
          >
            <Feather name="folder" size={14} color={colors.primary} />
          </View>
          <Text style={[styles.sectionTitle, { color: colors.foreground }]} numberOfLines={1}>
            {section.title}
          </Text>
          <View style={[styles.sectionCount, { backgroundColor: colors.muted }]}>
            <Text style={[styles.sectionCountText, { color: colors.mutedForeground }]}>
              {section.totalCount}
            </Text>
          </View>
          <Feather
            name={isExpanded ? "chevron-up" : "chevron-down"}
            size={16}
            color={colors.mutedForeground}
            style={styles.chevron}
          />
        </Pressable>
      );
    },
    [colors, toggleGroup, handleGroupLongPress]
  );

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      {/* Header */}
      <View
        style={[
          styles.header,
          { paddingTop: topPad + 12, borderBottomColor: colors.border, backgroundColor: colors.background },
        ]}
      >
        <Text style={[styles.title, { color: colors.foreground }]}>身份验证器</Text>
        <View style={styles.headerActions}>
          <Pressable
            onPress={() => router.push("/backup")}
            style={[styles.iconBtn, { backgroundColor: colors.muted }]}
            hitSlop={8}
          >
            <Feather name="settings" size={18} color={colors.mutedForeground} />
          </Pressable>
          <Pressable
            onPress={() => router.push("/add")}
            style={[styles.addBtn, { backgroundColor: colors.primary }]}
            hitSlop={8}
          >
            <Feather name="plus" size={22} color="#fff" />
          </Pressable>
        </View>
      </View>

      {/* Search bar */}
      {accounts.length > 0 && (
        <View style={[styles.searchContainer, { backgroundColor: colors.background, borderBottomColor: colors.border }]}>
          <View style={[styles.searchBar, { backgroundColor: colors.card, borderColor: colors.border }]}>
            <Feather name="search" size={16} color={colors.mutedForeground} />
            <TextInput
              ref={searchInputRef}
              style={[styles.searchInput, { color: colors.foreground }]}
              value={searchQuery}
              onChangeText={setSearchQuery}
              placeholder="搜索账户或服务…"
              placeholderTextColor={colors.mutedForeground}
              autoCorrect={false}
              autoCapitalize="none"
              returnKeyType="search"
            />
            {isSearching && (
              <Pressable onPress={clearSearch} hitSlop={8}>
                <Feather name="x-circle" size={16} color={colors.mutedForeground} />
              </Pressable>
            )}
          </View>
        </View>
      )}

      {/* Content */}
      {!loading && accounts.length === 0 ? (
        <View style={styles.empty}>
          <View style={[styles.emptyIcon, { backgroundColor: colors.primary + "18" }]}>
            <Feather name="shield" size={40} color={colors.primary} />
          </View>
          <Text style={[styles.emptyTitle, { color: colors.foreground }]}>暂无账户</Text>
          <Text style={[styles.emptySubtitle, { color: colors.mutedForeground }]}>
            点击 + 添加第一个账户
          </Text>
          <Pressable
            onPress={() => router.push("/add")}
            style={[styles.emptyBtn, { backgroundColor: colors.primary }]}
          >
            <Text style={styles.emptyBtnText}>添加账户</Text>
          </Pressable>
        </View>
      ) : isSearching ? (
        filteredAccounts.length === 0 ? (
          <View style={styles.empty}>
            <Feather name="search" size={36} color={colors.mutedForeground} />
            <Text style={[styles.emptyTitle, { color: colors.foreground }]}>无匹配结果</Text>
            <Text style={[styles.emptySubtitle, { color: colors.mutedForeground }]}>试试其他关键词</Text>
          </View>
        ) : (
          <FlatList
            data={filteredAccounts}
            keyExtractor={(item) => item.id}
            renderItem={({ item }) => (
              <AccountCard account={item} groups={groups} onDelete={removeAccount} onMoveToGroup={setMovingAccount} searchQuery={searchQuery} />
            )}
            contentContainerStyle={{ paddingTop: 10, paddingBottom: bottomPad + 24 }}
            keyboardShouldPersistTaps="handled"
            showsVerticalScrollIndicator={false}
          />
        )
      ) : (
        <SectionList
          sections={sections}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <AccountCard account={item} groups={groups} onDelete={removeAccount} onMoveToGroup={setMovingAccount} />
          )}
          renderSectionHeader={renderSectionHeader}
          stickySectionHeadersEnabled
          contentContainerStyle={{ paddingTop: 8, paddingBottom: bottomPad + 80 }}
          scrollEnabled={!!accounts.length}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        />
      )}

      {/* Scan button */}
      {accounts.length > 0 && !isSearching && (
        <View style={[styles.scanBtnContainer, { bottom: bottomPad + 16 }]}>
          <Pressable
            onPress={() => router.push("/scan")}
            style={({ pressed }) => [
              styles.scanBtn,
              { backgroundColor: colors.card, borderColor: colors.border, opacity: pressed ? 0.8 : 1 },
            ]}
          >
            <Feather name="camera" size={20} color={colors.primary} />
            <Text style={[styles.scanBtnText, { color: colors.primary }]}>扫描二维码</Text>
          </Pressable>
        </View>
      )}

      {/* Move to group modal */}
      <GroupSelectModal
        visible={!!movingAccount}
        groups={groups}
        selectedGroup={movingAccount?.group}
        onSelect={handleMoveToGroup}
        onClose={() => setMovingAccount(null)}
      />

      {/* Rename group modal */}
      <Modal
        visible={!!renameTarget}
        transparent
        animationType="fade"
        onRequestClose={() => setRenameTarget(null)}
      >
        <Pressable style={styles.renameBackdrop} onPress={() => setRenameTarget(null)} />
        <View style={[styles.renameDialogWrap, { pointerEvents: "box-none" }]}>
          <View style={[styles.renameDialog, { backgroundColor: colors.card, borderColor: colors.border }]}>
            <Text style={[styles.renameTitle, { color: colors.foreground }]}>重命名分组</Text>
            <TextInput
              style={[styles.renameInput, { backgroundColor: colors.background, borderColor: colors.border, color: colors.foreground }]}
              value={renameValue}
              onChangeText={setRenameValue}
              placeholder="输入新名称"
              placeholderTextColor={colors.mutedForeground}
              autoFocus
              selectTextOnFocus
              returnKeyType="done"
              onSubmitEditing={handleRenameConfirm}
            />
            <View style={styles.renameActions}>
              <Pressable
                onPress={() => setRenameTarget(null)}
                style={[styles.renameBtn, { backgroundColor: colors.muted }]}
              >
                <Text style={[styles.renameBtnText, { color: colors.mutedForeground }]}>取消</Text>
              </Pressable>
              <Pressable
                onPress={handleRenameConfirm}
                style={[styles.renameBtn, { backgroundColor: colors.primary }]}
              >
                <Text style={[styles.renameBtnText, { color: "#fff" }]}>确认</Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: 20,
    paddingBottom: 14,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  title: { fontSize: 26, fontFamily: "Inter_700Bold", letterSpacing: -0.5 },
  headerActions: { flexDirection: "row", alignItems: "center", gap: 8 },
  iconBtn: { width: 36, height: 36, borderRadius: 18, alignItems: "center", justifyContent: "center" },
  addBtn: { width: 40, height: 40, borderRadius: 20, alignItems: "center", justifyContent: "center" },
  searchContainer: { paddingHorizontal: 16, paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth },
  searchBar: { flexDirection: "row", alignItems: "center", gap: 8, borderWidth: 1, borderRadius: 12, paddingHorizontal: 12, paddingVertical: 9 },
  searchInput: { flex: 1, fontSize: 15, fontFamily: "Inter_400Regular", padding: 0 },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingHorizontal: 16,
    paddingVertical: 10,
    marginHorizontal: 16,
    marginTop: 10,
    borderRadius: 12,
  },
  sectionIconWrap: { width: 26, height: 26, borderRadius: 8, alignItems: "center", justifyContent: "center" },
  sectionTitle: { flex: 1, fontSize: 14, fontFamily: "Inter_600SemiBold" },
  sectionCount: { paddingHorizontal: 7, paddingVertical: 2, borderRadius: 8 },
  sectionCountText: { fontSize: 11, fontFamily: "Inter_600SemiBold" },
  chevron: { marginLeft: 2 },
  empty: { flex: 1, alignItems: "center", justifyContent: "center", paddingHorizontal: 40, gap: 12 },
  emptyIcon: { width: 80, height: 80, borderRadius: 40, alignItems: "center", justifyContent: "center", marginBottom: 8 },
  emptyTitle: { fontSize: 20, fontFamily: "Inter_700Bold", textAlign: "center" },
  emptySubtitle: { fontSize: 14, fontFamily: "Inter_400Regular", textAlign: "center", lineHeight: 20 },
  emptyBtn: { paddingHorizontal: 28, paddingVertical: 12, borderRadius: 24, marginTop: 8 },
  emptyBtnText: { fontSize: 15, fontFamily: "Inter_600SemiBold", color: "#ffffff" },
  scanBtnContainer: { position: "absolute", left: 0, right: 0, alignItems: "center" },
  scanBtn: { flexDirection: "row", alignItems: "center", gap: 8, paddingHorizontal: 20, paddingVertical: 10, borderRadius: 24, borderWidth: 1 },
  scanBtnText: { fontSize: 14, fontFamily: "Inter_600SemiBold" },
  // Rename modal
  renameBackdrop: { ...StyleSheet.absoluteFillObject, backgroundColor: "rgba(0,0,0,0.5)" },
  renameDialogWrap: { ...StyleSheet.absoluteFillObject, alignItems: "center", justifyContent: "center", paddingHorizontal: 32 },
  renameDialog: {
    width: "100%",
    borderRadius: 18,
    borderWidth: 1,
    padding: 20,
    gap: 16,
  },
  renameTitle: { fontSize: 16, fontFamily: "Inter_700Bold", textAlign: "center" },
  renameInput: {
    borderWidth: 1,
    borderRadius: 12,
    paddingHorizontal: 14,
    paddingVertical: 11,
    fontSize: 15,
    fontFamily: "Inter_400Regular",
  },
  renameActions: { flexDirection: "row", gap: 10 },
  renameBtn: { flex: 1, paddingVertical: 12, borderRadius: 12, alignItems: "center" },
  renameBtnText: { fontSize: 15, fontFamily: "Inter_600SemiBold" },
});
