import * as Clipboard from "expo-clipboard";
import * as Haptics from "expo-haptics";
import { router } from "expo-router";
import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  Alert,
  Animated,
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
const RING_SIZE = 36;
const STROKE = 3;
const RADIUS = (RING_SIZE - STROKE) / 2;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
const ACTION_WIDTH = 80;

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

            <View style={[styles.avatar, { backgroundColor: labelColor + "22" }]}>
              <Text style={[styles.avatarText, { color: labelColor }]}>{initials}</Text>
            </View>

            <View style={styles.info}>
              {!!account.issuer && (
                <HighlightText
                  text={account.issuer}
                  query={searchQuery}
                  style={[styles.issuer, { color: colors.mutedForeground }]}
                  highlightColor={labelColor}
                  numberOfLines={1}
                />
              )}
              <HighlightText
                text={account.account}
                query={searchQuery}
                style={[styles.accountName, { color: colors.foreground }]}
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
    borderRadius: 16,
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
    borderRadius: 16,
    borderWidth: 1,
    paddingHorizontal: 16,
    paddingVertical: 14,
    gap: 14,
    overflow: "hidden",
  },
  colorStripe: {
    position: "absolute",
    left: 0, top: 0, bottom: 0,
    width: 4,
    borderTopLeftRadius: 16,
    borderBottomLeftRadius: 16,
  },
  avatar: {
    width: 44, height: 44, borderRadius: 22,
    alignItems: "center", justifyContent: "center",
  },
  avatarText: { fontSize: 18, fontFamily: "Inter_700Bold" },
  info: { flex: 1, gap: 2 },
  issuer: {
    fontSize: 11, fontFamily: "Inter_500Medium",
    textTransform: "uppercase", letterSpacing: 0.8,
  },
  accountName: { fontSize: 13, fontFamily: "Inter_500Medium" },
  code: { fontSize: 28, fontFamily: "Inter_700Bold", letterSpacing: 2, marginTop: 2 },
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
