import { Feather } from "@expo/vector-icons";
import * as Clipboard from "expo-clipboard";
import CryptoJS from "crypto-js";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
import React, { useState } from "react";
import {
  Alert,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  Switch,
  Share,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { Account, useAccounts } from "@/context/AccountsContext";
import { SensitiveAuthGate } from "@/components/SensitiveAuthGate";
import { useLock } from "@/context/LockContext";
import { ThemePreference, useTheme } from "@/context/ThemeContext";
import { useColors } from "@/hooks/useColors";
import {
  decryptString,
  encryptString,
  formatRecoveryKey,
  generateRecoveryKey,
  isValidRecoveryKey,
  normalizeRecoveryKey,
  SecureEnvelope,
} from "@/utils/secureCrypto";

interface LegacyBackupPayload {
  version: 1;
  created: number;
  encrypted: boolean;
  data: string;
}

interface SecureBackupPayload {
  version: 2;
  created: number;
  encrypted: true;
  keyFormat: "base64url-256";
  data: SecureEnvelope;
}

export default function BackupScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { accounts, importAccounts } = useAccounts();
  const { themePreference, setThemePreference } = useTheme();
  const { appLockEnabled, setAppLockEnabled, systemAuthAvailable, lock } = useLock();

  const [exportLoading, setExportLoading] = useState(false);
  const [exportAuthVisible, setExportAuthVisible] = useState(false);
  const [recoveryModalVisible, setRecoveryModalVisible] = useState(false);
  const [recoveryKey, setRecoveryKey] = useState("");
  const [preparedBackup, setPreparedBackup] = useState("");
  const [recoveryKeyCopied, setRecoveryKeyCopied] = useState(false);

  const [importText, setImportText] = useState("");
  const [importRecoveryKey, setImportRecoveryKey] = useState("");
  const [importLoading, setImportLoading] = useState(false);
  const [importError, setImportError] = useState("");

  const topPad = Platform.OS === "web" ? 67 : insets.top;
  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  // ── Export ──────────────────────────────────────────────────────────────
  const prepareEncryptedExport = async () => {
    if (accounts.length === 0) {
      Alert.alert("暂无账户", "请先添加账户后再导出备份");
      return;
    }
    setExportLoading(true);
    try {
      const accountsJson = JSON.stringify(
        accounts.map(({ id, issuer, account, secret, group, color, createdAt }) => ({
          id, issuer, account, secret, group, color, createdAt,
        }))
      );

      const key = generateRecoveryKey();
      const payload: SecureBackupPayload = {
        version: 2,
        created: Date.now(),
        encrypted: true,
        keyFormat: "base64url-256",
        data: encryptString(accountsJson, key),
      };
      setRecoveryKey(key);
      setPreparedBackup(JSON.stringify(payload));
      setRecoveryKeyCopied(false);
      setRecoveryModalVisible(true);
    } catch {
      Alert.alert("导出失败", "请重试");
    } finally {
      setExportLoading(false);
    }
  };

  const handleExport = () => {
    if (accounts.length === 0) {
      Alert.alert("暂无账户", "请先添加账户后再导出备份");
      return;
    }
    setExportAuthVisible(true);
  };

  const copyRecoveryKey = async () => {
    await Clipboard.setStringAsync(formatRecoveryKey(recoveryKey));
    setRecoveryKeyCopied(true);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
  };

  const sharePreparedBackup = async () => {
    if (!preparedBackup || !recoveryKeyCopied) return;
    if (Platform.OS === "web") {
      await Clipboard.setStringAsync(preparedBackup);
      Alert.alert("已复制", "加密备份已复制到剪贴板");
    } else {
      await Share.share({ message: preparedBackup, title: "身份验证器加密备份" });
    }
    setRecoveryModalVisible(false);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
  };

  // ── Import ──────────────────────────────────────────────────────────────
  const handleImport = async () => {
    setImportError("");
    const text = importText.trim();
    if (!text) {
      setImportError("请粘贴备份数据");
      return;
    }
    setImportLoading(true);
    try {
      const payload = JSON.parse(text) as LegacyBackupPayload | SecureBackupPayload;

      let parsed: Account[];
      if (payload.version === 2) {
        if (!isValidRecoveryKey(importRecoveryKey)) {
          setImportError("请输入有效的恢复密钥");
          setImportLoading(false);
          return;
        }
        parsed = JSON.parse(
          decryptString(payload.data, normalizeRecoveryKey(importRecoveryKey)),
        );
      } else if (payload.version === 1 && payload.encrypted) {
        if (!importRecoveryKey.trim()) {
          setImportError("请输入旧版备份密码");
          setImportLoading(false);
          return;
        }
        const bytes = CryptoJS.AES.decrypt(payload.data, importRecoveryKey.trim());
        const decrypted = bytes.toString(CryptoJS.enc.Utf8);
        if (!decrypted) throw new Error("Wrong password");
        parsed = JSON.parse(decrypted);
      } else if (payload.version === 1) {
        parsed = JSON.parse(payload.data);
      } else {
        throw new Error("Unsupported backup version");
      }

      if (!Array.isArray(parsed)) throw new Error("Invalid format");

      const result = await importAccounts(parsed.map((acc) => ({
          issuer: acc.issuer ?? "",
          account: acc.account ?? "",
          secret: acc.secret,
          group: acc.group,
          color: acc.color,
          createdAt: acc.createdAt ?? Date.now(),
        })));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert(
        "导入成功",
        result.skipped > 0
          ? `已导入 ${result.imported} 个账户，跳过 ${result.skipped} 个重复账户`
          : `已成功导入 ${result.imported} 个账户`,
        [{ text: "确定", onPress: () => router.back() }]
      );
    } catch (e: unknown) {
      const msg = e instanceof Error && e.message === "Wrong password"
        ? "密码错误，请检查后重试"
        : "备份数据无效或已损坏";
      setImportError(msg);
    } finally {
      setImportLoading(false);
    }
  };

  return (
    <KeyboardAvoidingView
      style={[styles.container, { backgroundColor: colors.background }]}
      behavior={Platform.OS === "ios" ? "padding" : "height"}
    >
      {/* Header */}
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
          备份与恢复
        </Text>
        <View style={{ width: 24 }} />
      </View>

      <ScrollView
        contentContainerStyle={[styles.content, { paddingBottom: bottomPad + 32 }]}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        {/* ── Appearance Section ── */}
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}>
          <View style={styles.sectionHead}>
            <View style={[styles.iconWrap, { backgroundColor: colors.muted }]}>
              <Feather name="sun" size={18} color={colors.mutedForeground} />
            </View>
            <View style={styles.sectionHeadText}>
              <Text style={[styles.sectionTitle, { color: colors.foreground }]}>
                外观
              </Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}>
                选择界面主题
              </Text>
            </View>
          </View>
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <View style={styles.themeRow}>
            {(
              [
                { value: "system", icon: "monitor", label: "跟随系统" },
                { value: "light", icon: "sun", label: "浅色" },
                { value: "dark", icon: "moon", label: "深色" },
              ] as { value: ThemePreference; icon: string; label: string }[]
            ).map(({ value, icon, label }) => {
              const active = themePreference === value;
              return (
                <Pressable
                  key={value}
                  onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                    setThemePreference(value);
                  }}
                  style={[
                    styles.themeOption,
                    {
                      backgroundColor: active ? colors.primary : colors.background,
                      borderColor: active ? colors.primary : colors.border,
                    },
                  ]}
                >
                  <Feather
                    name={icon as any}
                    size={16}
                    color={active ? "#fff" : colors.mutedForeground}
                  />
                  <Text
                    style={[
                      styles.themeLabel,
                      { color: active ? "#fff" : colors.mutedForeground },
                    ]}
                  >
                    {label}
                  </Text>
                </Pressable>
              );
            })}
          </View>
        </View>

        {/* ── System authentication ── */}
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}> 
          <View style={styles.sectionHead}>
            <View style={[styles.iconWrap, { backgroundColor: colors.destructive + "18" }]}> 
              <Feather name="lock" size={18} color={colors.destructive} />
            </View>
            <View style={styles.sectionHeadText}>
              <Text style={[styles.sectionTitle, { color: colors.foreground }]}>系统身份认证</Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}> 
                指纹、面容或手机锁屏密码
              </Text>
            </View>
          </View>

          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <View style={styles.appLockRow}>
            <View style={styles.appLockText}>
              <Text style={[styles.appLockTitle, { color: colors.foreground }]}>进入 App 时验证身份</Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}>首次安装默认关闭；开启后，从后台返回超过一分钟需要验证。</Text>
            </View>
            <Switch
              value={appLockEnabled}
              onValueChange={(enabled) => void setAppLockEnabled(enabled)}
              disabled={!systemAuthAvailable}
              trackColor={{ false: colors.muted, true: colors.primary + "99" }}
              thumbColor={appLockEnabled ? colors.primary : "#f4f3f4"}
              accessibilityLabel="进入 App 时验证身份"
            />
          </View>
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <View style={[styles.secureNotice, { backgroundColor: colors.primary + "0D" }]}> 
            <Feather name="shield" size={16} color={colors.primary} />
            <Text style={[styles.secureNoticeText, { color: colors.mutedForeground }]}> 
              关闭应用锁不影响查看或复制密钥、导出备份时的系统身份验证。应用不会读取或保存你的系统 PIN。
            </Text>
          </View>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="立即锁定应用"
            disabled={!systemAuthAvailable}
            onPress={() => {
              lock();
              router.back();
            }}
            style={({ pressed }) => [
              styles.manualLockBtn,
              {
                backgroundColor: colors.background,
                borderColor: colors.border,
                opacity: !systemAuthAvailable ? 0.45 : pressed ? 0.78 : 1,
              },
            ]}
          >
            <Feather name="lock" size={16} color={colors.foreground} />
            <Text style={[styles.manualLockBtnText, { color: colors.foreground }]}>立即锁定应用</Text>
          </Pressable>
        </View>

        {/* ── Export Section ── */}
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}>
          <View style={styles.sectionHead}>
            <View style={[styles.iconWrap, { backgroundColor: colors.primary + "18" }]}>
              <Feather name="refresh-cw" size={18} color={colors.primary} />
            </View>
            <View style={styles.sectionHeadText}>
              <Text style={[styles.sectionTitle, { color: colors.foreground }]}>二维码安全迁移</Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}>两台设备面对面加密传输，不经过云端</Text>
            </View>
          </View>
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <Pressable
            onPress={() => router.push("/qr-transfer" as never)}
            style={({ pressed }) => [styles.actionBtn, { backgroundColor: colors.primary, opacity: pressed ? 0.85 : 1 }]}
          >
            <Feather name="maximize" size={16} color="#fff" />
            <Text style={styles.actionBtnText}>开始二维码迁移</Text>
          </Pressable>
        </View>

        {/* ── Export Section ── */}
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}>
          <View style={styles.sectionHead}>
            <View style={[styles.iconWrap, { backgroundColor: colors.primary + "18" }]}>
              <Feather name="upload-cloud" size={18} color={colors.primary} />
            </View>
            <View style={styles.sectionHeadText}>
              <Text style={[styles.sectionTitle, { color: colors.foreground }]}>
                导出备份
              </Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}>
                共 {accounts.length} 个账户
              </Text>
            </View>
          </View>

          <View style={[styles.divider, { backgroundColor: colors.border }]} />

          <View style={[styles.secureNotice, { backgroundColor: colors.primary + "12" }]}> 
            <Feather name="shield" size={16} color={colors.primary} />
            <Text style={[styles.secureNoticeText, { color: colors.mutedForeground }]}> 
              导出前需要验证身份。备份始终加密，并生成仅用于本次备份的恢复密钥。
            </Text>
          </View>

          <Pressable
            onPress={handleExport}
            disabled={exportLoading}
            style={({ pressed }) => [
              styles.actionBtn,
              {
                backgroundColor: exportLoading ? colors.primary + "88" : colors.primary,
                opacity: pressed ? 0.85 : 1,
              },
            ]}
          >
            <Feather name="share" size={16} color="#fff" />
            <Text style={styles.actionBtnText}>
              {exportLoading ? "导出中…" : "导出备份"}
            </Text>
          </Pressable>
        </View>

        {/* ── Import Section ── */}
        <View style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}>
          <View style={styles.sectionHead}>
            <View style={[styles.iconWrap, { backgroundColor: colors.accent + "18" }]}>
              <Feather name="download-cloud" size={18} color={colors.accent} />
            </View>
            <View style={styles.sectionHeadText}>
              <Text style={[styles.sectionTitle, { color: colors.foreground }]}>
                导入备份
              </Text>
              <Text style={[styles.sectionSub, { color: colors.mutedForeground }]}>
                粘贴备份数据以恢复账户
              </Text>
            </View>
          </View>

          <View style={[styles.divider, { backgroundColor: colors.border }]} />

          <Text style={[styles.fieldLabel, { color: colors.mutedForeground }]}>
            备份数据
          </Text>
          <TextInput
            style={[
              styles.input,
              styles.textArea,
              {
                backgroundColor: colors.background,
                borderColor: importError ? colors.destructive : colors.border,
                color: colors.foreground,
              },
            ]}
            value={importText}
            onChangeText={(t) => { setImportText(t); setImportError(""); }}
            placeholder='粘贴备份内容（{"version":1,...}）'
            placeholderTextColor={colors.mutedForeground}
            multiline
            autoCapitalize="none"
            autoCorrect={false}
          />

          <Text style={[styles.fieldLabel, { color: colors.mutedForeground, marginTop: 12 }]}>
            恢复密钥 / 旧版备份密码
          </Text>
          <TextInput
            style={[
              styles.input,
              {
                backgroundColor: colors.background,
                borderColor: colors.border,
                color: colors.foreground,
              },
            ]}
            value={importRecoveryKey}
            onChangeText={(value) => { setImportRecoveryKey(value); setImportError(""); }}
            placeholder="XXXX XXXX …"
            placeholderTextColor={colors.mutedForeground}
            secureTextEntry
            autoCapitalize="none"
            autoCorrect={false}
          />

          {!!importError && (
            <View style={styles.errorRow}>
              <Feather name="alert-circle" size={13} color={colors.destructive} />
              <Text style={[styles.errorText, { color: colors.destructive }]}>
                {importError}
              </Text>
            </View>
          )}

          <Pressable
            onPress={handleImport}
            disabled={importLoading}
            style={({ pressed }) => [
              styles.actionBtn,
              {
                backgroundColor: importLoading ? colors.accent + "88" : colors.accent,
                opacity: pressed ? 0.85 : 1,
              },
            ]}
          >
            <Feather name="download" size={16} color="#fff" />
            <Text style={styles.actionBtnText}>
              {importLoading ? "导入中…" : "导入备份"}
            </Text>
          </Pressable>
        </View>

        {/* Warning */}
        <View style={[styles.warningBox, { backgroundColor: colors.muted, borderColor: colors.border }]}>
          <Feather name="alert-triangle" size={14} color={colors.mutedForeground} />
          <Text style={[styles.warningText, { color: colors.mutedForeground }]}>
            备份内容始终加密。恢复密钥不会写入备份，请分开妥善保管。
          </Text>
        </View>
      </ScrollView>

      <SensitiveAuthGate
        visible={exportAuthVisible}
        reason="验证成功后才能导出包含 MFA 密钥的加密备份"
        onAuthenticated={() => {
          setExportAuthVisible(false);
          void prepareEncryptedExport();
        }}
        onCancel={() => setExportAuthVisible(false)}
      />

      <Modal
        visible={recoveryModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setRecoveryModalVisible(false)}
      >
        <View style={styles.modalBackdrop}>
          <View style={[styles.recoveryCard, { backgroundColor: colors.card, borderColor: colors.border }]}> 
            <View style={[styles.recoveryIcon, { backgroundColor: colors.primary + "18" }]}> 
              <Feather name="key" size={22} color={colors.primary} />
            </View>
            <Text style={[styles.recoveryTitle, { color: colors.foreground }]}>保存恢复密钥</Text>
            <Text style={[styles.recoveryDescription, { color: colors.mutedForeground }]}> 
              换机恢复时必须使用此密钥。它不会包含在备份中，丢失后无法找回。
            </Text>
            <Text selectable style={[styles.recoveryValue, { color: colors.foreground, backgroundColor: colors.background, borderColor: colors.border }]}> 
              {formatRecoveryKey(recoveryKey)}
            </Text>
            <Pressable
              onPress={copyRecoveryKey}
              style={({ pressed }) => [styles.actionBtn, { backgroundColor: colors.secondary, opacity: pressed ? 0.8 : 1 }]}
            >
              <Feather name={recoveryKeyCopied ? "check" : "copy"} size={16} color={colors.secondaryForeground} />
              <Text style={[styles.actionBtnText, { color: colors.secondaryForeground }]}> 
                {recoveryKeyCopied ? "恢复密钥已复制" : "复制恢复密钥"}
              </Text>
            </Pressable>
            <Pressable
              onPress={sharePreparedBackup}
              disabled={!recoveryKeyCopied}
              style={({ pressed }) => [styles.actionBtn, { backgroundColor: colors.primary, opacity: !recoveryKeyCopied ? 0.4 : pressed ? 0.8 : 1 }]}
            >
              <Feather name="share" size={16} color="#fff" />
              <Text style={styles.actionBtnText}>导出加密备份</Text>
            </Pressable>
            <Pressable onPress={() => setRecoveryModalVisible(false)} style={styles.modalCancel}>
              <Text style={[styles.modalCancelText, { color: colors.mutedForeground }]}>取消</Text>
            </Pressable>
          </View>
        </View>
      </Modal>
    </KeyboardAvoidingView>
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
  headerTitle: { fontSize: 17, fontFamily: "Inter_600SemiBold" },
  content: { padding: 16, gap: 16 },
  card: {
    borderRadius: 8,
    borderWidth: 1,
    padding: 16,
    gap: 10,
  },
  sectionHead: { flexDirection: "row", alignItems: "center", gap: 12 },
  iconWrap: { width: 38, height: 38, borderRadius: 10, alignItems: "center", justifyContent: "center" },
  sectionHeadText: { flex: 1, gap: 2 },
  sectionTitle: { fontSize: 15, fontFamily: "Inter_600SemiBold" },
  sectionSub: { fontSize: 12, fontFamily: "Inter_400Regular" },
  divider: { height: StyleSheet.hairlineWidth, marginVertical: 4 },
  fieldLabel: {
    fontSize: 11,
    fontFamily: "Inter_500Medium",
    textTransform: "uppercase",
    letterSpacing: 0.5,
  },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 15,
    fontFamily: "Inter_400Regular",
  },
  textArea: { minHeight: 80, textAlignVertical: "top" },
  hint: { fontSize: 11, fontFamily: "Inter_400Regular" },
  secureNotice: { flexDirection: "row", alignItems: "flex-start", gap: 8, borderRadius: 8, padding: 10 },
  secureNoticeText: { flex: 1, fontSize: 12, fontFamily: "Inter_400Regular", lineHeight: 18 },
  appLockRow: { flexDirection: "row", alignItems: "center", gap: 12 },
  appLockText: { flex: 1, gap: 3 },
  appLockTitle: { fontSize: 14, fontFamily: "Inter_600SemiBold" },
  errorRow: { flexDirection: "row", alignItems: "center", gap: 6 },
  errorText: { fontSize: 12, fontFamily: "Inter_400Regular" },
  themeRow: { flexDirection: "row", gap: 8 },
  themeOption: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 6,
    borderWidth: 1,
    borderRadius: 6,
    paddingVertical: 10,
  },
  themeLabel: { fontSize: 12, fontFamily: "Inter_500Medium" },
  manualLockBtn: { minHeight: 42, borderRadius: 8, borderWidth: 1, alignItems: "center", justifyContent: "center", flexDirection: "row", gap: 8 },
  manualLockBtnText: { fontSize: 14, fontFamily: "Inter_600SemiBold" },
  actionBtn: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    borderRadius: 8,
    paddingVertical: 13,
  },
  actionBtnText: { fontSize: 14, fontFamily: "Inter_600SemiBold", color: "#fff" },
  warningBox: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 10,
    borderRadius: 8,
    borderWidth: 1,
    padding: 12,
  },
  warningText: { flex: 1, fontSize: 12, fontFamily: "Inter_400Regular", lineHeight: 18 },
  modalBackdrop: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
    backgroundColor: "rgba(0,0,0,0.58)",
  },
  recoveryCard: { width: "100%", maxWidth: 400, borderWidth: 1, borderRadius: 8, padding: 22, gap: 12 },
  recoveryIcon: { width: 48, height: 48, borderRadius: 24, alignSelf: "center", alignItems: "center", justifyContent: "center" },
  recoveryTitle: { textAlign: "center", fontSize: 20, fontFamily: "Inter_700Bold" },
  recoveryDescription: { textAlign: "center", fontSize: 13, lineHeight: 19, fontFamily: "Inter_400Regular" },
  recoveryValue: { borderWidth: 1, borderRadius: 8, padding: 12, fontSize: 14, lineHeight: 21, fontFamily: "Inter_600SemiBold", textAlign: "center" },
  modalCancel: { alignSelf: "center", paddingHorizontal: 18, paddingVertical: 8 },
  modalCancelText: { fontSize: 14, fontFamily: "Inter_500Medium" },
});
