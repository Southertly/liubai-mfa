import { Feather } from "@expo/vector-icons";
import * as Haptics from "expo-haptics";
import { router, useLocalSearchParams } from "expo-router";
import React, { useCallback, useEffect, useState } from "react";
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

import { LabelColorOptions } from "@/components/LabelColorOptions";
import { PageHeader } from "@/components/PageHeader";
import { GroupSelectModal } from "@/components/GroupSelectModal";
import { SensitiveAuthGate } from "@/components/SensitiveAuthGate";
import { resolveColor } from "@/constants/labelColors";
import { Account, useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";
import { setSensitiveClipboard } from "@/utils/sensitiveClipboard";
import { generateTOTP } from "@/utils/totp";

function CodePreview({ account }: { account: Account }) {
  const colors = useColors();
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const code = generateTOTP(account.secret, account.period, account.digits, account.algorithm, now);
  const split = Math.ceil(code.length / 2);
  return <Text style={[styles.codePreview, { color: colors.foreground }]}>{code.slice(0, split)} {code.slice(split)}</Text>;
}

export default function EditAccountScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { accounts, groups, updateAccount } = useAccounts();

  const account = accounts.find((a) => a.id === id);

  const [issuer, setIssuer] = useState(account?.issuer ?? "");
  const [accountName, setAccountName] = useState(account?.account ?? "");
  const [selectedGroup, setSelectedGroup] = useState<string | undefined>(account?.group);
  const [selectedColor, setSelectedColor] = useState<string | undefined>(account?.color);
  const [groupModalVisible, setGroupModalVisible] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  // Secret reveal state
  const [secretRevealed, setSecretRevealed] = useState(false);
  const [secretVerifying, setSecretVerifying] = useState(false);
  const [secretCopied, setSecretCopied] = useState(false);
  const [authGateVisible, setAuthGateVisible] = useState(false);

  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  if (!account) {
    return (
      <View style={[styles.container, { backgroundColor: colors.background, justifyContent: "center", alignItems: "center" }]}>
        <Text style={{ color: colors.mutedForeground }}>账户不存在</Text>
      </View>
    );
  }

  const accentColor = resolveColor(selectedColor, colors.primary);

  // ── Save ─────────────────────────────────────────────────────────────────
  const handleSave = async () => {
    setError("");
    if (!accountName.trim() && !issuer.trim()) {
      setError("请输入账户名称或服务名称");
      return;
    }
    setLoading(true);
    try {
      await updateAccount(account.id, {
        issuer: issuer.trim(),
        account: accountName.trim() || issuer.trim(),
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

  // ── Secret reveal with biometrics ────────────────────────────────────────
  const handleRevealSecret = useCallback(() => {
    if (secretRevealed) {
      setSecretRevealed(false);
      return;
    }
    setSecretVerifying(true);
    setAuthGateVisible(true);
  }, [secretRevealed]);

  const handleCopySecret = useCallback(async () => {
    await setSensitiveClipboard(account.secret, 60_000);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    setSecretCopied(true);
    setTimeout(() => setSecretCopied(false), 2000);
  }, [account.secret]);

  // Format secret in groups of 4 for readability
  const formattedSecret = account.secret.replace(/(.{4})/g, "$1 ").trim();

  return (
    <>
      <KeyboardAvoidingView
        style={[styles.container, { backgroundColor: colors.background }]}
        behavior={Platform.OS === "ios" ? "padding" : "height"}
      >
        <PageHeader title="编辑账户" />

        <ScrollView
          contentContainerStyle={[styles.content, { paddingBottom: bottomPad + 24 }]}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          {/* ── Secret card ── */}
          <View
            style={[
              styles.secretCard,
              {
                backgroundColor: colors.card,
                borderColor: secretRevealed ? accentColor + "55" : colors.border,
              },
            ]}
          >
            <CodePreview account={account} />
            {/* Header row */}
            <View style={styles.secretHeaderRow}>
              <View style={styles.secretLabelRow}>
                <Feather
                  name={secretRevealed ? "unlock" : "lock"}
                  size={13}
                  color={secretRevealed ? accentColor : colors.mutedForeground}
                />
                <Text style={[styles.secretLabel, { color: secretRevealed ? accentColor : colors.mutedForeground }]}>
                  密钥{secretRevealed ? "（只读）" : ""}
                </Text>
              </View>

              {/* Reveal / hide toggle */}
              <Pressable
                onPress={handleRevealSecret}
                disabled={secretVerifying}
                style={({ pressed }) => [
                  styles.revealBtn,
                  {
                    backgroundColor: secretRevealed ? colors.muted : accentColor + "18",
                    borderColor: secretRevealed ? colors.border : accentColor + "44",
                    opacity: pressed || secretVerifying ? 0.7 : 1,
                  },
                ]}
              >
                <Feather
                  name={secretVerifying ? "loader" : secretRevealed ? "eye-off" : "eye"}
                  size={13}
                  color={secretRevealed ? colors.mutedForeground : accentColor}
                />
                <Text
                  style={[
                    styles.revealBtnText,
                    { color: secretRevealed ? colors.mutedForeground : accentColor },
                  ]}
                >
                  {secretVerifying ? "验证中…" : secretRevealed ? "隐藏" : "验证后查看"}
                </Text>
              </Pressable>
            </View>

            {/* Secret value */}
            {secretRevealed ? (
              <>
                <Text
                  style={[styles.secretValueRevealed, { color: colors.foreground }]}
                  selectable
                >
                  {formattedSecret}
                </Text>

                {/* Copy button */}
                <Pressable
                  onPress={handleCopySecret}
                  style={({ pressed }) => [
                    styles.copySecretBtn,
                    {
                      backgroundColor: secretCopied ? colors.success + "18" : accentColor + "14",
                      borderColor: secretCopied ? colors.success + "55" : accentColor + "44",
                      opacity: pressed ? 0.8 : 1,
                    },
                  ]}
                >
                  <Feather
                    name={secretCopied ? "check" : "copy"}
                    size={14}
                    color={secretCopied ? colors.success : accentColor}
                  />
                  <Text
                    style={[
                      styles.copySecretBtnText,
                      { color: secretCopied ? colors.success : accentColor },
                    ]}
                  >
                    {secretCopied ? "已复制" : "复制密钥"}
                  </Text>
                </Pressable>
              </>
            ) : (
              <Text style={[styles.secretDots, { color: colors.mutedForeground }]}>
                {"•  ".repeat(8).trim()}
              </Text>
            )}

            {/* Security note */}
            {secretRevealed && (
              <View style={styles.secretNote}>
                <Feather name="alert-triangle" size={11} color={colors.mutedForeground} />
                <Text style={[styles.secretNoteText, { color: colors.mutedForeground }]}>
                  请勿将密钥分享给任何人
                </Text>
              </View>
            )}
          </View>

          {/* ── Editable fields ── */}
          <View style={[styles.form, { backgroundColor: colors.card, borderColor: colors.border }]}>
            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>服务名称</Text>
              <TextInput
                style={[styles.input, { backgroundColor: colors.input, borderColor: colors.border, color: colors.foreground }]}
                value={issuer}
                onChangeText={setIssuer}
                placeholder="如 Google"
                placeholderTextColor={colors.mutedForeground}
                autoCapitalize="words"
                returnKeyType="next"
              />
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>账户名称</Text>
              <TextInput
                style={[
                  styles.input,
                  { backgroundColor: colors.input, borderColor: error ? colors.destructive : colors.border, color: colors.foreground },
                ]}
                value={accountName}
                onChangeText={(t) => { setAccountName(t); setError(""); }}
                placeholder="user@example.com"
                placeholderTextColor={colors.mutedForeground}
                autoCapitalize="none"
                keyboardType="email-address"
                returnKeyType="next"
              />
              {!!error && (
                <Text style={[styles.errorText, { color: colors.destructive }]}>{error}</Text>
              )}
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>分组（可选）</Text>
              <Pressable
                onPress={() => setGroupModalVisible(true)}
                style={({ pressed }) => [
                  styles.input, styles.rowField,
                  { backgroundColor: colors.input, borderColor: colors.border, opacity: pressed ? 0.8 : 1 },
                ]}
              >
                <Feather name="folder" size={16} color={selectedGroup ? accentColor : colors.mutedForeground} />
                <Text style={[styles.rowText, { color: selectedGroup ? colors.foreground : colors.mutedForeground }]}>
                  {selectedGroup ?? "未分组"}
                </Text>
                <Feather name="chevron-right" size={16} color={colors.mutedForeground} />
              </Pressable>
            </View>

            <View style={styles.fieldGroup}>
              <Text style={[styles.label, { color: colors.mutedForeground }]}>标签颜色（可选）</Text>
              <LabelColorOptions selectedColor={selectedColor} onSelect={setSelectedColor} />
            </View>
          </View>

          <Pressable
            onPress={handleSave}
            disabled={loading}
            style={({ pressed }) => [
              styles.saveBtn,
              { backgroundColor: loading ? accentColor + "88" : accentColor, opacity: pressed ? 0.85 : 1 },
            ]}
          >
            <Text style={styles.saveBtnText}>{loading ? "保存中…" : "保存更改"}</Text>
          </Pressable>
        </ScrollView>
      </KeyboardAvoidingView>

      <GroupSelectModal
        visible={groupModalVisible}
        groups={groups}
        selectedGroup={selectedGroup}
        onSelect={(g) => { setSelectedGroup(g); setGroupModalVisible(false); }}
        onClose={() => setGroupModalVisible(false)}
      />

      <SensitiveAuthGate
        visible={authGateVisible}
        reason="验证成功后才能查看和复制 MFA 密钥"
        onAuthenticated={() => {
          setAuthGateVisible(false);
          setSecretVerifying(false);
          setSecretRevealed(true);
          Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        }}
        onCancel={() => {
          setAuthGateVisible(false);
          setSecretVerifying(false);
        }}
      />
    </>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  content: { paddingHorizontal: 16, paddingTop: 8, gap: 18 },

  // Secret card
  codePreview: { fontSize: 34, fontFamily: "Inter_700Bold", textAlign: "center", marginBottom: 8 },
  secretCard: { borderRadius: 8, borderWidth: 1, padding: 14, gap: 10 },
  secretHeaderRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  secretLabelRow: { flexDirection: "row", alignItems: "center", gap: 6 },
  secretLabel: { fontSize: 11, fontFamily: "Inter_500Medium" },
  revealBtn: {
    flexDirection: "row",
    alignItems: "center",
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 10,
    minHeight: 44,
    borderRadius: 6,
    borderWidth: 1,
  },
  revealBtnText: { fontSize: 12, fontFamily: "Inter_600SemiBold" },
  secretDots: { fontSize: 22, letterSpacing: 4, fontFamily: "Inter_400Regular" },
  secretValueRevealed: {
    fontSize: 15,
    fontFamily: "Inter_500Medium",
    letterSpacing: 2.5,
    lineHeight: 24,
  },
  copySecretBtn: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 6,
    borderRadius: 8,
    borderWidth: 1,
    paddingVertical: 9,
  },
  copySecretBtnText: { fontSize: 13, fontFamily: "Inter_600SemiBold" },
  secretNote: { flexDirection: "row", alignItems: "center", gap: 5 },
  secretNoteText: { fontSize: 11, fontFamily: "Inter_400Regular" },

  // Form
  form: { borderWidth: 1, borderRadius: 8, padding: 16, gap: 10 },
  fieldGroup: { gap: 6 },
  label: { fontSize: 12, fontFamily: "Inter_500Medium" },
  input: { minHeight: 48, borderWidth: 0, borderRadius: 8, paddingHorizontal: 14, paddingVertical: 12, fontSize: 15, fontFamily: "Inter_400Regular" },
  rowField: { flexDirection: "row", alignItems: "center", gap: 10 },
  rowText: { flex: 1, fontSize: 15, fontFamily: "Inter_400Regular" },
  errorText: { fontSize: 12, fontFamily: "Inter_400Regular", marginTop: 2 },
  saveBtn: { borderRadius: 8, paddingVertical: 15, alignItems: "center", marginTop: 8 },
  saveBtnText: { fontSize: 16, fontFamily: "Inter_600SemiBold", color: "#ffffff" },
});
