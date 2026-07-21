import AsyncStorage from "@react-native-async-storage/async-storage";
import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Linking,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useColors } from "@/hooks/useColors";

const PRIVACY_CONSENT_KEY = "tick_mfa_privacy_consent_v1";
const PRIVACY_POLICY_URL = "https://www.sediary.com/tick-mfa/privacy/";

export function PrivacyConsentGate({ children }: { children: React.ReactNode }) {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const [checkingConsent, setCheckingConsent] = useState(true);
  const [hasConsented, setHasConsented] = useState(false);
  const [declined, setDeclined] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        const storedConsent = await AsyncStorage.getItem(PRIVACY_CONSENT_KEY);
        if (mounted) setHasConsented(storedConsent === "accepted");
      } catch {
        if (mounted) {
          setHasConsented(false);
          setErrorMessage("无法读取隐私政策同意状态，请重新确认后继续。");
        }
      } finally {
        if (mounted) setCheckingConsent(false);
      }
    })();

    return () => {
      mounted = false;
    };
  }, []);

  const openPrivacyPolicy = useCallback(async () => {
    setErrorMessage(null);
    try {
      await Linking.openURL(PRIVACY_POLICY_URL);
    } catch {
      setErrorMessage("暂时无法打开隐私政策链接，请检查网络后重试。");
    }
  }, []);

  const acceptPrivacyPolicy = useCallback(async () => {
    setErrorMessage(null);
    try {
      await AsyncStorage.setItem(PRIVACY_CONSENT_KEY, "accepted");
      setHasConsented(true);
    } catch {
      setErrorMessage("保存同意状态失败，请稍后重试。");
    }
  }, []);

  const declinePrivacyPolicy = useCallback(() => {
    setDeclined(true);
    setErrorMessage("您需要同意隐私政策后才能使用留白 MFA。");
  }, []);

  if (checkingConsent) {
    return (
      <View style={[styles.loadingScreen, { backgroundColor: colors.background }]}>
        <ActivityIndicator color={colors.primary} />
      </View>
    );
  }

  if (hasConsented) return <>{children}</>;

  return (
    <View style={[styles.blockingScreen, { backgroundColor: colors.background }]}>
      <Modal transparent visible animationType="fade" statusBarTranslucent>
        <View
          style={[
            styles.scrim,
            {
              paddingTop: insets.top + 24,
              paddingBottom: insets.bottom + 24,
            },
          ]}
        >
          <View
            style={[
              styles.card,
              {
                backgroundColor: colors.card,
                borderColor: colors.border,
              },
            ]}
          >
            <Text style={[styles.title, { color: colors.foreground }]}>
              留白 MFA 隐私政策
            </Text>
            <Text style={[styles.body, { color: colors.mutedForeground }]}>
              欢迎使用留白 MFA。我们重视您的隐私和 MFA 密钥安全。首次使用前，请您阅读并同意
              <Text
                accessibilityRole="link"
                onPress={openPrivacyPolicy}
                style={[styles.link, { color: colors.primary }]}
              >
                《留白 MFA 隐私政策》
              </Text>
              。本应用主要在您的设备本地处理 MFA 账户与验证码数据，未同意前不会进入主功能。
            </Text>
            <Text style={[styles.notice, { color: colors.mutedForeground }]}>
              如您不同意隐私政策，将无法继续使用留白 MFA。
            </Text>

            {(declined || errorMessage) && (
              <Text style={[styles.error, { color: colors.destructive }]}>
                {errorMessage}
              </Text>
            )}

            <Pressable
              accessibilityRole="button"
              onPress={openPrivacyPolicy}
              style={({ pressed }) => [
                styles.linkButton,
                {
                  borderColor: colors.border,
                  backgroundColor: pressed ? colors.muted : "transparent",
                },
              ]}
            >
              <Text style={[styles.linkButtonText, { color: colors.primary }]}>
                阅读隐私政策
              </Text>
            </Pressable>

            <View style={styles.actions}>
              <Pressable
                accessibilityRole="button"
                onPress={declinePrivacyPolicy}
                style={({ pressed }) => [
                  styles.secondaryButton,
                  {
                    borderColor: colors.border,
                    backgroundColor: pressed ? colors.muted : "transparent",
                  },
                ]}
              >
                <Text style={[styles.secondaryButtonText, { color: colors.foreground }]}>
                  不同意
                </Text>
              </Pressable>
              <Pressable
                accessibilityRole="button"
                onPress={acceptPrivacyPolicy}
                style={({ pressed }) => [
                  styles.primaryButton,
                  {
                    backgroundColor: pressed ? colors.primary + "cc" : colors.primary,
                  },
                ]}
              >
                <Text
                  style={[
                    styles.primaryButtonText,
                    { color: colors.primaryForeground },
                  ]}
                >
                  同意并继续
                </Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  loadingScreen: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
  },
  blockingScreen: {
    flex: 1,
  },
  scrim: {
    alignItems: "center",
    backgroundColor: "rgba(0, 0, 0, 0.48)",
    flex: 1,
    justifyContent: "center",
    paddingHorizontal: 20,
  },
  card: {
    borderRadius: 24,
    borderWidth: StyleSheet.hairlineWidth,
    maxWidth: 420,
    padding: 22,
    width: "100%",
  },
  title: {
    fontFamily: "Inter_700Bold",
    fontSize: 22,
    letterSpacing: -0.4,
    marginBottom: 12,
  },
  body: {
    fontFamily: "Inter_400Regular",
    fontSize: 15,
    lineHeight: 24,
  },
  link: {
    fontFamily: "Inter_600SemiBold",
    textDecorationLine: "underline",
  },
  notice: {
    fontFamily: "Inter_400Regular",
    fontSize: 13,
    lineHeight: 20,
    marginTop: 12,
  },
  error: {
    fontFamily: "Inter_500Medium",
    fontSize: 13,
    lineHeight: 20,
    marginTop: 12,
  },
  linkButton: {
    alignItems: "center",
    borderRadius: 14,
    borderWidth: 1,
    marginTop: 18,
    paddingVertical: 12,
  },
  linkButtonText: {
    fontFamily: "Inter_600SemiBold",
    fontSize: 15,
  },
  actions: {
    flexDirection: "row",
    gap: 12,
    marginTop: 14,
  },
  secondaryButton: {
    alignItems: "center",
    borderRadius: 14,
    borderWidth: 1,
    flex: 1,
    paddingVertical: 13,
  },
  secondaryButtonText: {
    fontFamily: "Inter_600SemiBold",
    fontSize: 15,
  },
  primaryButton: {
    alignItems: "center",
    borderRadius: 14,
    flex: 1,
    paddingVertical: 13,
  },
  primaryButtonText: {
    fontFamily: "Inter_700Bold",
    fontSize: 15,
  },
});
