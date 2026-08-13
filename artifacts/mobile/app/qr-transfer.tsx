import { Feather } from "@expo/vector-icons";
import { CameraView, useCameraPermissions } from "expo-camera";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
import React, { useEffect, useRef, useState } from "react";
import { Alert, Pressable, StyleSheet, Text, View } from "react-native";
import QRCode from "react-native-qrcode-svg";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { SensitiveAuthGate } from "@/components/SensitiveAuthGate";
import { useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";
import { encryptString, generateRecoveryKey, isValidRecoveryKey } from "@/utils/secureCrypto";
import {
  assembleTransferPackage,
  buildPairingQr,
  encodeTransferPackage,
  parsePairingQr,
  parseTransferChunk,
  validatePortableAccounts,
} from "@/utils/qrTransfer";
import { qrTransferCrypto } from "@/utils/qrTransferCrypto";

type Mode = "choice" | "receive-pair" | "receive-scan" | "send-scan" | "send-show";
type AuthIntent = "receive" | "send" | null;

interface ReceiveSession {
  sessionId: string;
  publicKey: string;
  expiresAt: number;
}

export default function QrTransferScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { accounts, importAccounts } = useAccounts();
  const [permission, requestPermission] = useCameraPermissions();
  const [mode, setMode] = useState<Mode>("choice");
  const [authIntent, setAuthIntent] = useState<AuthIntent>(null);
  const [receiveSession, setReceiveSession] = useState<ReceiveSession | null>(null);
  const [pairingQr, setPairingQr] = useState("");
  const [transferQrs, setTransferQrs] = useState<string[]>([]);
  const [activeQrIndex, setActiveQrIndex] = useState(0);
  const [autoAdvance, setAutoAdvance] = useState(true);
  const [receivedCount, setReceivedCount] = useState(0);
  const [expectedCount, setExpectedCount] = useState(0);
  const [busy, setBusy] = useState(false);
  const scanning = useRef(false);
  const receiveSessionRef = useRef<ReceiveSession | null>(null);
  const receivedChunks = useRef<Map<number, string>>(new Map());

  useEffect(() => {
    receiveSessionRef.current = receiveSession;
  }, [receiveSession]);

  useEffect(() => {
    if (mode !== "send-show" || !autoAdvance || transferQrs.length <= 1) return;
    const timer = setInterval(() => {
      setActiveQrIndex((current) => (current + 1) % transferQrs.length);
    }, 1500);
    return () => clearInterval(timer);
  }, [autoAdvance, mode, transferQrs.length]);

  useEffect(() => () => {
    const session = receiveSessionRef.current;
    if (session) void qrTransferCrypto.clearSession(session.sessionId);
    receivedChunks.current.clear();
  }, []);

  const ensureCamera = async (): Promise<boolean> => {
    if (permission?.granted) return true;
    const result = await requestPermission();
    if (!result.granted) Alert.alert("需要相机权限", "请允许使用相机扫描账户迁移二维码");
    return result.granted;
  };

  const startReceive = async () => {
    setBusy(true);
    try {
      const session = await qrTransferCrypto.createReceiveSession();
      receivedChunks.current.clear();
      setReceivedCount(0);
      setExpectedCount(0);
      setReceiveSession(session);
      setPairingQr(buildPairingQr(session));
      setMode("receive-pair");
    } catch (error) {
      Alert.alert("无法开始接收", error instanceof Error ? error.message : "请重试");
    } finally {
      setBusy(false);
    }
  };

  const startSend = async () => {
    if (accounts.length === 0) {
      Alert.alert("暂无账户", "请先添加账户后再迁移");
      return;
    }
    if (await ensureCamera()) {
      scanning.current = false;
      setMode("send-scan");
    }
  };

  const prepareTransfer = async (value: string) => {
    setBusy(true);
    try {
      const pairing = parsePairingQr(value);
      const transferKey = generateRecoveryKey();
      const plaintext = JSON.stringify(accounts.map(({ issuer, account, secret, createdAt, group, color }) => ({
        issuer, account, secret, createdAt, group, color,
      })));
      const wrappedKey = await qrTransferCrypto.wrapKey(pairing.publicKey, transferKey);
      const qrs = encodeTransferPackage({
        version: 1,
        sessionId: pairing.sessionId,
        wrappedKey,
        data: encryptString(plaintext, transferKey),
      });
      setTransferQrs(qrs);
      setActiveQrIndex(0);
      setAutoAdvance(true);
      setMode("send-show");
    } catch (error) {
      Alert.alert("配对二维码无效", error instanceof Error ? error.message : "请重新扫描");
      scanning.current = false;
    } finally {
      setBusy(false);
    }
  };

  const finishReceive = async (session: ReceiveSession, total: number) => {
    setBusy(true);
    try {
      const payload = assembleTransferPackage(session.sessionId, receivedChunks.current, total);
      const transferKey = await qrTransferCrypto.unwrapKey(session.sessionId, payload.wrappedKey);
      if (!isValidRecoveryKey(transferKey)) throw new Error("临时传输密钥无效");
      const plaintext = await qrTransferCrypto.decryptTransferEnvelope(JSON.stringify(payload.data), transferKey);
      const portable = validatePortableAccounts(JSON.parse(plaintext));
      const result = await importAccounts(portable);
      setReceiveSession(null);
      receivedChunks.current.clear();
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert(
        "迁移完成",
        `新增 ${result.imported} 个账户，跳过 ${result.skipped} 个重复账户`,
        [{ text: "完成", onPress: () => router.back() }],
      );
    } catch (error) {
      Alert.alert("迁移失败", error instanceof Error ? error.message : "二维码数据无效或已损坏");
      resetReceive();
    } finally {
      setBusy(false);
    }
  };

  const handleBarcode = ({ data }: { data: string }) => {
    if (scanning.current || busy) return;
    scanning.current = true;
    if (mode === "send-scan") {
      void prepareTransfer(data);
      return;
    }
    if (mode !== "receive-scan" || !receiveSession) {
      scanning.current = false;
      return;
    }
    try {
      const chunk = parseTransferChunk(data);
      if (chunk.sessionId !== receiveSession.sessionId) throw new Error("这不是当前设备的账户二维码");
      if (expectedCount !== 0 && expectedCount !== chunk.total) throw new Error("二维码不属于同一次迁移");
      receivedChunks.current.set(chunk.index, chunk.data);
      setExpectedCount(chunk.total);
      setReceivedCount(receivedChunks.current.size);
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
      if (receivedChunks.current.size === chunk.total) {
        void finishReceive(receiveSession, chunk.total);
        return;
      }
    } catch (error) {
      Alert.alert("无法读取二维码", error instanceof Error ? error.message : "请重新扫描");
    }
    setTimeout(() => { scanning.current = false; }, 650);
  };

  const openReceiveScanner = async () => {
    if (await ensureCamera()) {
      scanning.current = false;
      setMode("receive-scan");
    }
  };

  const resetReceive = () => {
    const session = receiveSessionRef.current;
    if (session) void qrTransferCrypto.clearSession(session.sessionId);
    setReceiveSession(null);
    setPairingQr("");
    receivedChunks.current.clear();
    setReceivedCount(0);
    setExpectedCount(0);
    scanning.current = false;
    setMode("choice");
  };

  const renderCamera = (title: string, subtitle: string) => (
    <View style={styles.cameraPage}>
      <Text style={[styles.title, { color: colors.foreground }]}>{title}</Text>
      <Text style={[styles.subtitle, { color: colors.mutedForeground }]}>{subtitle}</Text>
      <View style={[styles.cameraFrame, { borderColor: colors.primary }]}>
        <CameraView
          style={StyleSheet.absoluteFill}
          barcodeScannerSettings={{ barcodeTypes: ["qr"] }}
          onBarcodeScanned={handleBarcode}
        />
      </View>
      {mode === "receive-scan" && expectedCount > 0 && (
        <Text style={[styles.progress, { color: colors.primary }]}>已接收 {receivedCount}/{expectedCount}</Text>
      )}
      <Pressable onPress={() => mode === "receive-scan" ? setMode("receive-pair") : setMode("choice")} style={styles.textButton}>
        <Text style={{ color: colors.mutedForeground }}>返回</Text>
      </Pressable>
    </View>
  );

  return (
    <View style={[styles.container, { backgroundColor: colors.background, paddingTop: insets.top + 12, paddingBottom: insets.bottom + 16 }]}>
      <View style={styles.header}>
        <Pressable onPress={() => router.back()} hitSlop={10}><Feather name="x" size={24} color={colors.mutedForeground} /></Pressable>
        <Text style={[styles.headerTitle, { color: colors.foreground }]}>二维码安全迁移</Text>
        <View style={{ width: 24 }} />
      </View>

      {mode === "choice" && (
        <View style={styles.choice}>
          <View style={[styles.notice, { backgroundColor: colors.primary + "12", borderColor: colors.primary + "33" }]}>
            <Feather name="shield" size={22} color={colors.primary} />
            <Text style={[styles.noticeText, { color: colors.mutedForeground }]}>账户使用一次性公钥加密传输，不经过云端。完成后临时私钥立即销毁。</Text>
          </View>
          <Pressable disabled={busy} onPress={() => setAuthIntent("receive")} style={[styles.primaryButton, { backgroundColor: colors.primary }]}>
            <Feather name="download" size={20} color="#fff" /><Text style={styles.primaryText}>接收账户</Text>
          </Pressable>
          <Pressable disabled={busy || accounts.length === 0} onPress={() => setAuthIntent("send")} style={[styles.secondaryButton, { backgroundColor: colors.card, borderColor: colors.border, opacity: accounts.length === 0 ? 0.45 : 1 }]}>
            <Feather name="upload" size={20} color={colors.foreground} /><Text style={[styles.secondaryText, { color: colors.foreground }]}>发送 {accounts.length} 个账户</Text>
          </Pressable>
        </View>
      )}

      {mode === "receive-pair" && (
        <View style={styles.qrPage}>
          <Text style={[styles.title, { color: colors.foreground }]}>让旧设备扫描配对码</Text>
          <Text style={[styles.subtitle, { color: colors.mutedForeground }]}>配对码只包含一次性公钥，5 分钟后失效。</Text>
          <View style={styles.qrCard}>{pairingQr ? <QRCode value={pairingQr} size={270} ecl="L" /> : null}</View>
          <Pressable onPress={() => void openReceiveScanner()} style={[styles.primaryButton, { backgroundColor: colors.primary }]}>
            <Feather name="camera" size={18} color="#fff" /><Text style={styles.primaryText}>开始连续扫描</Text>
          </Pressable>
          <Pressable onPress={resetReceive} style={styles.textButton}><Text style={{ color: colors.mutedForeground }}>取消本次接收</Text></Pressable>
        </View>
      )}

      {mode === "send-scan" && renderCamera("扫描新设备的配对码", "只接受本应用生成、尚未过期的配对二维码")}
      {mode === "receive-scan" && renderCamera("连续扫描账户二维码", "保持镜头对准旧设备，无需点击下一张；三张二维码会自动收集")}

      {mode === "send-show" && transferQrs.length > 0 && (
        <View style={styles.qrPage}>
          <Text style={[styles.title, { color: colors.foreground }]}>让新设备扫描账户码</Text>
          <Text style={[styles.subtitle, { color: colors.mutedForeground }]}>仅配对设备持有解密私钥。请保持两台设备在身边。</Text>
          <View style={styles.qrCard}>
            <QRCode key={`transfer-qr-${activeQrIndex}`} value={transferQrs[activeQrIndex]} size={270} ecl="L" />
          </View>
          <Text style={[styles.progress, { color: colors.primary }]}>第 {activeQrIndex + 1}/{transferQrs.length} 张</Text>
          {transferQrs.length > 1 && (
            <View style={styles.qrControls}>
              <Pressable onPress={() => setActiveQrIndex((activeQrIndex - 1 + transferQrs.length) % transferQrs.length)} style={[styles.smallButton, { borderColor: colors.border }]}><Text style={{ color: colors.foreground }}>上一张</Text></Pressable>
              <Pressable onPress={() => setAutoAdvance((value) => !value)} style={[styles.smallButton, { borderColor: colors.border }]}><Text style={{ color: colors.foreground }}>{autoAdvance ? "暂停轮播" : "自动轮播"}</Text></Pressable>
              <Pressable onPress={() => setActiveQrIndex((activeQrIndex + 1) % transferQrs.length)} style={[styles.smallButton, { borderColor: colors.border }]}><Text style={{ color: colors.foreground }}>下一张</Text></Pressable>
            </View>
          )}
          <Pressable onPress={() => { setTransferQrs([]); setMode("choice"); }} style={styles.textButton}><Text style={{ color: colors.mutedForeground }}>完成并清除二维码</Text></Pressable>
        </View>
      )}

      <SensitiveAuthGate
        visible={authIntent !== null}
        reason={authIntent === "receive" ? "验证成功后才能创建一次性接收私钥" : "验证成功后才能读取并发送 MFA 账户"}
        onAuthenticated={() => {
          const intent = authIntent;
          setAuthIntent(null);
          if (intent === "receive") void startReceive();
          if (intent === "send") void startSend();
        }}
        onCancel={() => setAuthIntent(null)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: { height: 48, paddingHorizontal: 20, flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  headerTitle: { fontSize: 17, fontFamily: "Inter_600SemiBold" },
  choice: { flex: 1, justifyContent: "center", padding: 24, gap: 14 },
  notice: { borderWidth: 1, borderRadius: 8, padding: 12, flexDirection: "row", alignItems: "center", gap: 12, marginBottom: 12 },
  noticeText: { flex: 1, fontSize: 13, lineHeight: 20, fontFamily: "Inter_400Regular" },
  primaryButton: { minHeight: 50, borderRadius: 8, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 9, paddingHorizontal: 16 },
  primaryText: { color: "#fff", fontSize: 15, fontFamily: "Inter_600SemiBold" },
  secondaryButton: { minHeight: 50, borderRadius: 8, borderWidth: 1, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 9, paddingHorizontal: 16 },
  secondaryText: { fontSize: 15, fontFamily: "Inter_600SemiBold" },
  qrPage: { flex: 1, alignItems: "center", justifyContent: "center", padding: 20, gap: 14 },
  title: { fontSize: 20, fontFamily: "Inter_700Bold", textAlign: "center" },
  subtitle: { fontSize: 13, lineHeight: 19, fontFamily: "Inter_400Regular", textAlign: "center", maxWidth: 340 },
  qrCard: { padding: 14, borderRadius: 8, backgroundColor: "#fff" },
  progress: { fontSize: 14, fontFamily: "Inter_600SemiBold" },
  textButton: { paddingHorizontal: 18, paddingVertical: 10 },
  cameraPage: { flex: 1, alignItems: "center", justifyContent: "center", padding: 20, gap: 14 },
  cameraFrame: { width: 300, height: 300, overflow: "hidden", borderRadius: 8, borderWidth: 3 },
  qrControls: { flexDirection: "row", alignItems: "center", gap: 8 },
  smallButton: { borderWidth: 1, borderRadius: 8, paddingHorizontal: 12, paddingVertical: 9 },
});
