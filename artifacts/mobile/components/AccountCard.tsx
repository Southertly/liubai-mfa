import * as Clipboard from "expo-clipboard";
import * as Haptics from "expo-haptics";
import { FontAwesome6 } from "@expo/vector-icons";
import { router } from "expo-router";
import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  Alert,
  Animated,
  Image,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { Swipeable } from "react-native-gesture-handler";
import Svg, { Circle } from "react-native-svg";

import { ColorPickerModal } from "@/components/ColorPickerModal";
import { HighlightText } from "@/components/HighlightText";
import { resolveColor } from "@/constants/labelColors";
import { Account, useAccounts } from "@/context/AccountsContext";
import { useColors } from "@/hooks/useColors";
import { generateTOTP, getTimeRemaining } from "@/utils/totp";

interface Props {
  account: Account;
  groups: string[];
  onDelete: (id: string) => void;
  onMoveToGroup: (account: Account) => void;
  searchQuery?: string;
}

const PERIOD = 30;
const RING_SIZE = 40;
const STROKE = 3;
const RADIUS = (RING_SIZE - STROKE) / 2;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
const ACTION_WIDTH = 80;

// Google Identity 官方 80x80 中性圆形品牌图标（PNG @2x）。
const GOOGLE_ICON_DATA_URI = "data:image/png;base64,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";

type ServiceIconName = React.ComponentProps<typeof FontAwesome6>["name"];

interface ServiceIconRule {
  aliases: string[];
  icon: ServiceIconName;
  color: string;
}

const SERVICE_ICON_RULES: ServiceIconRule[] = [
  { aliases: ["google", "gmail", "谷歌"], icon: "google", color: "#4285F4" },
  { aliases: ["microsoft", "outlook", "office 365", "azure", "微软"], icon: "microsoft", color: "#737373" },
  { aliases: ["apple", "icloud", "苹果"], icon: "apple", color: "#111111" },
  { aliases: ["github"], icon: "github", color: "#24292F" },
  { aliases: ["amazon", "aws"], icon: "aws", color: "#FF9900" },
  { aliases: ["cloudflare"], icon: "cloudflare", color: "#F48120" },
  { aliases: ["facebook"], icon: "facebook-f", color: "#1877F2" },
  { aliases: ["twitter", "x.com"], icon: "x-twitter", color: "#111111" },
];

function serviceIconFor(account: Account): ServiceIconRule | undefined {
  const source = `${account.issuer} ${account.account}`.toLocaleLowerCase();
  return SERVICE_ICON_RULES.find((rule) =>
    rule.aliases.some((alias) => source.includes(alias))
  );
}

function isGoogleAccount(account: Account): boolean {
  const source = `${account.issuer} ${account.account}`.toLocaleLowerCase();
  return ["google", "gmail", "谷歌"].some((alias) => source.includes(alias));
}

export function AccountCard({ account, groups, onDelete, onMoveToGroup, searchQuery = "" }: Props) {
  const colors = useColors();
  const { updateAccount } = useAccounts();
  const [code, setCode] = useState(() => generateTOTP(account.secret));
  const [remaining, setRemaining] = useState(() => getTimeRemaining());
  const scaleAnim = useRef(new Animated.Value(1)).current;
  const copyFeedbackOpacity = useRef(new Animated.Value(0)).current;
  const copyFeedbackScale = useRef(new Animated.Value(0.8)).current;
  const [colorPickerVisible, setColorPickerVisible] = useState(false);
  const swipeableRef = useRef<Swipeable>(null);

  const labelColor = resolveColor(account.color, colors.primary);

  useEffect(() => {
    const tick = () => {
      const rem = getTimeRemaining(PERIOD);
      setRemaining(rem);
      if (rem === PERIOD) setCode(generateTOTP(account.secret));
    };
    const interval = setInterval(tick, 1000);
    return () => clearInterval(interval);
  }, [account.secret]);

  const progress = remaining / PERIOD;
  const strokeDashoffset = CIRCUMFERENCE * (1 - progress);
  const isLow = remaining <= 5;
  const ringColor = isLow ? colors.destructive : labelColor;

  const handleCopy = useCallback(async () => {
    await Clipboard.setStringAsync(code);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);

    Animated.sequence([
      Animated.timing(scaleAnim, { toValue: 0.96, duration: 80, useNativeDriver: true }),
      Animated.timing(scaleAnim, { toValue: 1, duration: 120, useNativeDriver: true }),
    ]).start();

    Animated.sequence([
      Animated.parallel([
        Animated.timing(copyFeedbackOpacity, { toValue: 1, duration: 120, useNativeDriver: true }),
        Animated.spring(copyFeedbackScale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 8 }),
      ]),
      Animated.delay(900),
      Animated.parallel([
        Animated.timing(copyFeedbackOpacity, { toValue: 0, duration: 200, useNativeDriver: true }),
        Animated.timing(copyFeedbackScale, { toValue: 0.8, duration: 200, useNativeDriver: true }),
      ]),
    ]).start();
  }, [code, scaleAnim, copyFeedbackOpacity, copyFeedbackScale]);

  const handleLongPress = useCallback(() => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    Alert.alert(
      account.issuer || account.account,
      account.group ? `分组：${account.group}` : "未分组",
      [
        { text: "更改标签颜色", onPress: () => setColorPickerVisible(true) },
        { text: "移动到分组", onPress: () => onMoveToGroup(account) },
        { text: "取消", style: "cancel" },
      ]
    );
  }, [account, onMoveToGroup]);

  const handleColorSelect = useCallback(
    async (colorId: string | undefined) => {
      await updateAccount(account.id, { color: colorId });
    },
    [account.id, updateAccount]
  );

  // ── Swipe actions ──────────────────────────────────────────────────────

  /** Right-swipe reveals: Edit (left action panel) */
  const renderLeftActions = useCallback(
    (progress: Animated.AnimatedInterpolation<number>) => {
      const translateX = progress.interpolate({
        inputRange: [0, 1],
        outputRange: [-ACTION_WIDTH, 0],
      });
      return (
        <Animated.View style={[styles.leftAction, { transform: [{ translateX }] }]}>
          <Pressable
            style={[styles.actionBtn, { backgroundColor: labelColor }]}
            onPress={() => {
              swipeableRef.current?.close();
              router.push(`/edit/${account.id}`);
            }}
          >
            <Text style={styles.actionIcon}>✏️</Text>
            <Text style={styles.actionLabel}>编辑</Text>
          </Pressable>
        </Animated.View>
      );
    },
    [account.id, labelColor]
  );

  /** Left-swipe reveals: Delete (right action panel) */
  const renderRightActions = useCallback(
    (progress: Animated.AnimatedInterpolation<number>) => {
      const translateX = progress.interpolate({
        inputRange: [0, 1],
        outputRange: [ACTION_WIDTH, 0],
      });
      return (
        <Animated.View style={[styles.rightAction, { transform: [{ translateX }] }]}>
          <Pressable
            style={[styles.actionBtn, { backgroundColor: colors.destructive }]}
            onPress={() => {
              swipeableRef.current?.close();
              Alert.alert(
                "删除账户",
                `确定要删除「${account.issuer || account.account}」吗？此操作不可撤销。`,
                [
                  { text: "取消", style: "cancel" },
                  {
                    text: "删除",
                    style: "destructive",
                    onPress: () => onDelete(account.id),
                  },
                ]
              );
            }}
          >
            <Text style={styles.actionIcon}>🗑️</Text>
            <Text style={styles.actionLabel}>删除</Text>
          </Pressable>
        </Animated.View>
      );
    },
    [account, colors.destructive, onDelete]
  );

  const formattedCode = code.slice(0, 3) + " " + code.slice(3);
  const initials = (account.issuer || account.account || "?").charAt(0).toUpperCase();
  const serviceIcon = serviceIconFor(account);
  const showGoogleIcon = isGoogleAccount(account);

  return (
    <>
      <Swipeable
        ref={swipeableRef}
        friction={2}
        leftThreshold={40}
        rightThreshold={40}
        renderLeftActions={renderLeftActions}
        renderRightActions={renderRightActions}
        overshootLeft={false}
        overshootRight={false}
        containerStyle={styles.swipeContainer}
      >
        <Animated.View style={{ transform: [{ scale: scaleAnim }] }}>
          <Pressable
            onPress={handleCopy}
            onLongPress={handleLongPress}
            style={({ pressed }) => [
              styles.card,
              {
                backgroundColor: colors.card,
                borderColor: colors.border,
                opacity: pressed ? 0.9 : 1,
              },
            ]}
          >
            {/* Colored left accent stripe */}
            <View style={[styles.colorStripe, { backgroundColor: labelColor }]} />

            <View
              style={[
                styles.avatar,
                {
                  backgroundColor: showGoogleIcon
                    ? "transparent"
                    : serviceIcon
                      ? serviceIcon.color + "18"
                      : labelColor + "22",
                },
              ]}
            >
              {showGoogleIcon ? (
                <>
                  <Text style={[styles.avatarText, { color: "#4285F4" }]}>G</Text>
                  <Image
                    accessibilityLabel="Google"
                    source={{ uri: GOOGLE_ICON_DATA_URI }}
                    style={[styles.brandImage, styles.brandImageOverlay]}
                    resizeMode="contain"
                  />
                </>
              ) : serviceIcon ? (
                <FontAwesome6 name={serviceIcon.icon} size={21} color={serviceIcon.color} />
              ) : (
                <Text style={[styles.avatarText, { color: labelColor }]}>{initials}</Text>
              )}
            </View>

            <View style={styles.info}>
              {!!account.issuer && (
                <HighlightText
                  text={account.issuer}
                  query={searchQuery}
                  style={[styles.issuer, { color: colors.foreground }]}
                  highlightColor={labelColor}
                  numberOfLines={1}
                />
              )}
              <HighlightText
                text={account.account}
                query={searchQuery}
                style={[styles.accountName, { color: colors.mutedForeground }]}
                highlightColor={labelColor}
                numberOfLines={1}
              />
              <Text style={[styles.code, { color: labelColor }]}>{formattedCode}</Text>
            </View>

            {/* "已复制" feedback badge */}
            <Animated.View
              style={[
                styles.copyBadge,
                {
                  backgroundColor: labelColor,
                  opacity: copyFeedbackOpacity,
                  transform: [{ scale: copyFeedbackScale }],
                },
              ]}
            >
              <Text style={styles.copyBadgeText}>已复制</Text>
            </Animated.View>

            <View style={styles.timerContainer}>
              <Svg width={RING_SIZE} height={RING_SIZE}>
                <Circle
                  cx={RING_SIZE / 2} cy={RING_SIZE / 2} r={RADIUS}
                  stroke={colors.border} strokeWidth={STROKE} fill="none"
                />
                <Circle
                  cx={RING_SIZE / 2} cy={RING_SIZE / 2} r={RADIUS}
                  stroke={ringColor} strokeWidth={STROKE} fill="none"
                  strokeDasharray={CIRCUMFERENCE}
                  strokeDashoffset={strokeDashoffset}
                  strokeLinecap="round"
                  rotation="-90"
                  origin={`${RING_SIZE / 2}, ${RING_SIZE / 2}`}
                />
              </Svg>
              <Text style={[styles.timerText, { color: isLow ? colors.destructive : colors.mutedForeground }]}>
                {remaining}
              </Text>
            </View>
          </Pressable>
        </Animated.View>
      </Swipeable>

      <ColorPickerModal
        visible={colorPickerVisible}
        selectedColor={account.color}
        onSelect={handleColorSelect}
        onClose={() => setColorPickerVisible(false)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  swipeContainer: {
    marginHorizontal: 16,
    marginVertical: 5,
    borderRadius: 8,
    overflow: "hidden",
  },
  leftAction: {
    width: ACTION_WIDTH,
    justifyContent: "center",
  },
  rightAction: {
    width: ACTION_WIDTH,
    justifyContent: "center",
  },
  actionBtn: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 4,
  },
  actionIcon: { fontSize: 20 },
  actionLabel: {
    fontSize: 12,
    fontFamily: "Inter_600SemiBold",
    color: "#fff",
  },
  card: {
    flexDirection: "row",
    alignItems: "center",
    borderRadius: 8,
    borderWidth: 1,
    paddingHorizontal: 12,
    paddingVertical: 12,
    gap: 12,
    overflow: "hidden",
  },
  colorStripe: {
    position: "absolute",
    left: 0, top: 0, bottom: 0,
    width: 4,
    borderTopLeftRadius: 8,
    borderBottomLeftRadius: 8,
  },
  avatar: {
    width: 44, height: 44, borderRadius: 22,
    alignItems: "center", justifyContent: "center",
  },
  brandImage: { width: 44, height: 44 },
  brandImageOverlay: { position: "absolute" },
  avatarText: { fontSize: 18, fontFamily: "Inter_700Bold" },
  info: { flex: 1, gap: 2 },
  issuer: {
    fontSize: 15, fontFamily: "Inter_700Bold",
    letterSpacing: 0.1,
  },
  accountName: { fontSize: 13, fontFamily: "Inter_500Medium" },
  code: { fontSize: 22, fontFamily: "Inter_700Bold", letterSpacing: 0, marginTop: 2 },
  timerContainer: {
    alignItems: "center", justifyContent: "center",
    width: RING_SIZE, height: RING_SIZE,
  },
  timerText: { position: "absolute", fontSize: 10, fontFamily: "Inter_600SemiBold" },
  copyBadge: {
    position: "absolute",
    right: 52,
    top: "50%",
    marginTop: -13,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 20,
    alignItems: "center",
    justifyContent: "center",
  },
  copyBadgeText: { fontSize: 12, fontFamily: "Inter_600SemiBold", color: "#fff" },
});
