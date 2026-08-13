import { Feather } from "@expo/vector-icons";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
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

import { ColorPickerModal } from "@/components/ColorPickerModal";
import { GroupSelectModal } from "@/components/GroupSelectModal";
import { LABEL_COLORS, resolveColor } from "@/constants/labelColors";
import { useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";
import { generateTOTP } from "@/utils/totp";

export default function AddAccountScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { addAccount, groups } = useAccounts();

  const [issuer, setIssuer] = useState("");
  const [account, setAccount] = useState("");
  const [secret, setSecret] = useState("");
  const [selectedGroup, setSelectedGroup] = useState<string | undefined>();
  const [selectedColor, setSelectedColor] = useState<string | undefined>();
  const [groupModalVisible, setGroupModalVisible] = useState(false);
  const [colorModalVisible, setColorModalVisible] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const topPad = Platform.OS === "web" ? 67 : insets.top;
  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  const handleAdd = async () => {
    setError("");
    if (!secret.trim()) {
      setError("请输入密钥");
      return;
    }
    if (!account.trim() && !issuer.trim()) {
      setError("请输入账户名称或服务名称");
      return;
    }
    const testCode = generateTOTP(secret.trim().replace(/\s/g, ""), 30, 6, "SHA1");
    if (testCode === "------") {
      setError("密钥无效，请检查后重试");
      return;
    }
    setLoading(true);
    try {
      await addAccount({
        issuer: issuer.trim(),
        account: account.trim() || issuer.trim(),
        secret: secret.trim().replace(/\s/g, "").toUpperCase(),
        algorithm: "SHA1",
        digits: 6,
        period: 30,
        group: selectedGroup,
        color: selectedColor,
      });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.back();
    } catch {
      setError("保存失败，请重试");
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <KeyboardAvoidingView
        style={[styles.container, { backgroundColor: colors.background }]}
        behavior={Platform.OS === "ios" ? "padding" : "height"}
      >
        <View
          style={[
            styles.header,
            {
              paddingTop: topPad + 12,
              borderBottomColor: colors.border,
              backgroundColor: colors.background,
            },
          ]}
        >
          <Pressable onPress={() => router.back()} hitSlop={8}>
            <Feather name="x" size={24} color={colors.mutedForeground} />
          </Pressable>
          <Text style={[styles.headerTitle, { color: colors.foreground }]}>
            添加账户
          </Text>
          <View style={{ width: 24 }} />
        </View>

        <ScrollView
          contentContainerStyle={[
            styles.content,
            { paddingBottom: bottomPad + 24 },
          ]}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          <View style={styles.scanPrompt}>
            <Pressable
              onPress={() => router.replace("/scan")}
              style={[
                styles.scanBtn,
                {
                  backgroundColor: colors.primary + "18",
                  borderColor: colors.primary + "44",
                },
              ]}
            >
              <Feather name="camera" size={20} color={colors.primary} />
              <Text style={[styles.scanBtnText, { color: colors.primary }]}>
                扫描二维码添加
              </Text>
            </Pressable>
          </View>

          <View style={styles.dividerRow}>
            <View
              style={[styles.divider, { backgroundColor: colors.border }]}
            />
            <Text
              style={[styles.dividerText, { color: colors.mutedForeground }]}
            >
              或手动输入
            </Text>
            <View
              style={[styles.divider, { backgroundColor: colors.border }]}
            />
          </View>

          <View style={styles.form}>
            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>
                服务名称（如 Google、GitHub）
              </Text>
              <TextInput
                style={[
                  styles.input,
                  {
                    backgroundColor: colors.card,
                    borderColor: colors.border,
                    color: colors.foreground,
                  },
                ]}
                value={issuer}
                onChangeText={setIssuer}
                placeholder="如 Google"
                placeholderTextColor={colors.mutedForeground}
                autoCapitalize="words"
                returnKeyType="next"
              />
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>
                账户名称
              </Text>
              <TextInput
                style={[
                  styles.input,
                  {
                    backgroundColor: colors.card,
                    borderColor: colors.border,
                    color: colors.foreground,
                  },
                ]}
                value={account}
                onChangeText={setAccount}
                placeholder="user@example.com（可选）"
                placeholderTextColor={colors.mutedForeground}
                autoCapitalize="none"
                keyboardType="email-address"
                returnKeyType="next"
              />
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>
                密钥 *
              </Text>
              <TextInput
                style={[
                  styles.input,
                  styles.secretInput,
                  {
                    backgroundColor: colors.card,
                    borderColor: error ? colors.destructive : colors.border,
                    color: colors.foreground,
                  },
                ]}
                value={secret}
                onChangeText={(t) => {
                  setSecret(t);
                  setError("");
                }}
                placeholder="JBSWY3DPEHPK3PXP"
                placeholderTextColor={colors.mutedForeground}
                autoCapitalize="characters"
                autoCorrect={false}
                returnKeyType="done"
                onSubmitEditing={handleAdd}
              />
              {!!error && (
                <Text
                  style={[styles.errorText, { color: colors.destructive }]}
                >
                  {error}
                </Text>
              )}
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>
                分组（可选）
              </Text>
              <Pressable
                onPress={() => setGroupModalVisible(true)}
                style={({ pressed }) => [
                  styles.input,
                  styles.groupRow,
                  {
                    backgroundColor: colors.card,
                    borderColor: colors.border,
                    opacity: pressed ? 0.8 : 1,
                  },
                ]}
              >
                <Feather
                  name="folder"
                  size={16}
                  color={
                    selectedGroup ? colors.primary : colors.mutedForeground
                  }
                />
                <Text
                  style={[
                    styles.groupText,
                    {
                      color: selectedGroup
                        ? colors.foreground
                        : colors.mutedForeground,
                    },
                  ]}
                >
                  {selectedGroup ?? "不分组"}
                </Text>
                <Feather
                  name="chevron-right"
                  size={16}
                  color={colors.mutedForeground}
                />
              </Pressable>
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>
                标签颜色（可选）
              </Text>
              <Pressable
                onPress={() => setColorModalVisible(true)}
                style={({ pressed }) => [
                  styles.input,
                  styles.groupRow,
                  {
                    backgroundColor: colors.card,
                    borderColor: colors.border,
                    opacity: pressed ? 0.8 : 1,
                  },
                ]}
              >
                <View
                  style={[
                    styles.colorDot,
                    {
                      backgroundColor: selectedColor
                        ? resolveColor(selectedColor, colors.primary)
                        : colors.muted,
                      borderColor: selectedColor ? "transparent" : colors.border,
                    },
                  ]}
                />
                <Text
                  style={[
                    styles.groupText,
                    {
                      color: selectedColor
                        ? colors.foreground
                        : colors.mutedForeground,
                    },
                  ]}
                >
                  {selectedColor
                    ? LABEL_COLORS.find((c) => c.id === selectedColor)?.name ?? "自定义"
                    : "无标签"}
                </Text>
                <Feather
                  name="chevron-right"
                  size={16}
                  color={colors.mutedForeground}
                />
              </Pressable>
            </View>
          </View>

          <Pressable
            onPress={handleAdd}
            disabled={loading}
            style={({ pressed }) => [
              styles.addBtn,
              {
                backgroundColor: loading
                  ? colors.primary + "88"
                  : colors.primary,
                opacity: pressed ? 0.85 : 1,
              },
            ]}
          >
            <Text style={styles.addBtnText}>
              {loading ? "添加中…" : "添加账户"}
            </Text>
          </Pressable>
        </ScrollView>
      </KeyboardAvoidingView>

      <GroupSelectModal
        visible={groupModalVisible}
        groups={groups}
        selectedGroup={selectedGroup}
        onSelect={(g) => {
          setSelectedGroup(g);
          setGroupModalVisible(false);
        }}
        onClose={() => setGroupModalVisible(false)}
      />

      <ColorPickerModal
        visible={colorModalVisible}
        selectedColor={selectedColor}
        onSelect={(c) => setSelectedColor(c)}
        onClose={() => setColorModalVisible(false)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: 20,
    paddingBottom: 14,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  headerTitle: {
    fontSize: 17,
    fontFamily: "Inter_600SemiBold",
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 24,
    gap: 20,
  },
  scanPrompt: {
    alignItems: "center",
  },
  scanBtn: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingHorizontal: 24,
    paddingVertical: 14,
    borderRadius: 8,
    borderWidth: 1,
    width: "100%",
    justifyContent: "center",
  },
  scanBtnText: {
    fontSize: 15,
    fontFamily: "Inter_600SemiBold",
  },
  dividerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  divider: {
    flex: 1,
    height: StyleSheet.hairlineWidth,
  },
  dividerText: {
    fontSize: 12,
    fontFamily: "Inter_400Regular",
  },
  form: {
    gap: 16,
  },
  fieldGroup: {
    gap: 6,
  },
  label: {
    fontSize: 12,
    fontFamily: "Inter_500Medium",
    textTransform: "uppercase",
    letterSpacing: 0.6,
  },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 15,
    fontFamily: "Inter_400Regular",
  },
  secretInput: {
    fontFamily: "Inter_500Medium",
    letterSpacing: 1,
  },
  groupRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  groupText: {
    flex: 1,
    fontSize: 15,
    fontFamily: "Inter_400Regular",
  },
  colorDot: {
    width: 20,
    height: 20,
    borderRadius: 8,
    borderWidth: 1.5,
  },
  errorText: {
    fontSize: 12,
    fontFamily: "Inter_400Regular",
    marginTop: 2,
  },
  addBtn: {
    borderRadius: 8,
    paddingVertical: 15,
    alignItems: "center",
    marginTop: 8,
  },
  addBtnText: {
    fontSize: 16,
    fontFamily: "Inter_600SemiBold",
    color: "#ffffff",
  },
});
