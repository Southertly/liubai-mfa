import { Feather } from "@expo/vector-icons";
import { CameraView, useCameraPermissions } from "expo-camera";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
import React, { useRef, useState } from "react";
import {
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";
import { parseOtpAuthUri } from "@/utils/totp";

export default function ScanScreen() {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const { addAccount } = useAccounts();
  const [permission, requestPermission] = useCameraPermissions();
  const [scanned, setScanned] = useState(false);
  const [error, setError] = useState("");
  const processingRef = useRef(false);

  const topPad = Platform.OS === "web" ? 67 : insets.top;
  const bottomPad = Platform.OS === "web" ? 34 : insets.bottom;

  const handleBarCodeScanned = async ({ data }: { data: string }) => {
    if (processingRef.current || scanned) return;
    processingRef.current = true;
    setScanned(true);

    const parsed = parseOtpAuthUri(data);
    if (!parsed) {
      setError("无效的验证码二维码，请重试");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setTimeout(() => {
        setScanned(false);
        setError("");
        processingRef.current = false;
      }, 2000);
      return;
    }

    try {
      await addAccount(parsed);
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.back();
    } catch {
      setError("保存失败，请重试");
      setTimeout(() => {
        setScanned(false);
        setError("");
        processingRef.current = false;
      }, 2000);
    }
  };

  if (!permission) {
    return (
      <View style={[styles.center, { backgroundColor: colors.background }]}>
        <Text style={[styles.text, { color: colors.foreground }]}>
          正在检查相机权限…
        </Text>
      </View>
    );
  }

  if (!permission.granted) {
    return (
      <View style={[styles.center, { backgroundColor: colors.background }]}>
        <Feather name="camera-off" size={48} color={colors.mutedForeground} />
        <Text
          style={[styles.permissionTitle, { color: colors.foreground }]}
        >
          需要相机权限
        </Text>
        <Text
          style={[styles.permissionSub, { color: colors.mutedForeground }]}
        >
          请允许访问相机以扫描二维码
        </Text>
        <Pressable
          onPress={requestPermission}
          style={[styles.permBtn, { backgroundColor: colors.primary }]}
        >
          <Text style={styles.permBtnText}>允许访问相机</Text>
        </Pressable>
        <Pressable onPress={() => router.back()} style={styles.cancelLink}>
          <Text
            style={[styles.cancelLinkText, { color: colors.mutedForeground }]}
          >
            返回
          </Text>
        </Pressable>
      </View>
    );
  }

  return (
    <View style={[styles.container, { backgroundColor: "#000" }]}>
      <CameraView
        style={StyleSheet.absoluteFill}
        facing="back"
        barcodeScannerSettings={{ barcodeTypes: ["qr"] }}
        onBarcodeScanned={scanned ? undefined : handleBarCodeScanned}
      />

      <View style={[styles.overlay, StyleSheet.absoluteFill]}>
        <View
          style={[
            styles.topBar,
            { paddingTop: topPad + 12 },
          ]}
        >
          <Pressable
            onPress={() => router.back()}
            style={styles.closeBtn}
            hitSlop={8}
          >
            <Feather name="x" size={24} color="#fff" />
          </Pressable>
          <Text style={styles.topTitle}>扫描二维码</Text>
          <View style={{ width: 40 }} />
        </View>

        <View style={styles.scanArea}>
          <View style={styles.scanFrame}>
            <View style={[styles.corner, styles.cornerTL]} />
            <View style={[styles.corner, styles.cornerTR]} />
            <View style={[styles.corner, styles.cornerBL]} />
            <View style={[styles.corner, styles.cornerBR]} />
          </View>
        </View>

        <View style={[styles.bottomArea, { paddingBottom: bottomPad + 24 }]}>
          {error ? (
            <View style={styles.errorBadge}>
              <Feather name="alert-circle" size={16} color="#fff" />
              <Text style={styles.errorBadgeText}>{error}</Text>
            </View>
          ) : (
            <Text style={styles.hint}>
              将相机对准验证器二维码
            </Text>
          )}
        </View>
      </View>
    </View>
  );
}

const FRAME = 240;
const CORNER = 24;
const BORDER = 3;

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  center: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 32,
    gap: 16,
  },
  text: {
    fontSize: 16,
    fontFamily: "Inter_400Regular",
  },
  permissionTitle: {
    fontSize: 20,
    fontFamily: "Inter_700Bold",
    textAlign: "center",
  },
  permissionSub: {
    fontSize: 14,
    fontFamily: "Inter_400Regular",
    textAlign: "center",
    lineHeight: 20,
  },
  permBtn: {
    paddingHorizontal: 28,
    paddingVertical: 12,
    borderRadius: 24,
    marginTop: 8,
  },
  permBtnText: {
    fontSize: 15,
    fontFamily: "Inter_600SemiBold",
    color: "#ffffff",
  },
  cancelLink: {
    padding: 8,
  },
  cancelLinkText: {
    fontSize: 14,
    fontFamily: "Inter_400Regular",
  },
  overlay: {
    justifyContent: "space-between",
  },
  topBar: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: 20,
    paddingBottom: 16,
  },
  closeBtn: {
    width: 40,
    height: 40,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(0,0,0,0.4)",
    borderRadius: 20,
  },
  topTitle: {
    fontSize: 17,
    fontFamily: "Inter_600SemiBold",
    color: "#fff",
  },
  scanArea: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  scanFrame: {
    width: FRAME,
    height: FRAME,
  },
  corner: {
    position: "absolute",
    width: CORNER,
    height: CORNER,
    borderColor: "#fff",
  },
  cornerTL: {
    top: 0,
    left: 0,
    borderTopWidth: BORDER,
    borderLeftWidth: BORDER,
    borderTopLeftRadius: 6,
  },
  cornerTR: {
    top: 0,
    right: 0,
    borderTopWidth: BORDER,
    borderRightWidth: BORDER,
    borderTopRightRadius: 6,
  },
  cornerBL: {
    bottom: 0,
    left: 0,
    borderBottomWidth: BORDER,
    borderLeftWidth: BORDER,
    borderBottomLeftRadius: 6,
  },
  cornerBR: {
    bottom: 0,
    right: 0,
    borderBottomWidth: BORDER,
    borderRightWidth: BORDER,
    borderBottomRightRadius: 6,
  },
  bottomArea: {
    alignItems: "center",
    paddingHorizontal: 32,
  },
  hint: {
    fontSize: 14,
    fontFamily: "Inter_400Regular",
    color: "rgba(255,255,255,0.75)",
    textAlign: "center",
  },
  errorBadge: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    backgroundColor: "rgba(239,68,68,0.85)",
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderRadius: 12,
  },
  errorBadgeText: {
    fontSize: 13,
    fontFamily: "Inter_500Medium",
    color: "#fff",
  },
});
