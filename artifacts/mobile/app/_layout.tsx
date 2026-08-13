import {
  Inter_400Regular,
  Inter_500Medium,
  Inter_600SemiBold,
  Inter_700Bold,
  useFonts,
} from "@expo-google-fonts/inter";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StatusBar } from "expo-status-bar";
import { Stack } from "expo-router";
import * as SplashScreen from "expo-splash-screen";
import React, { useEffect } from "react";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { KeyboardProvider } from "react-native-keyboard-controller";
import { SafeAreaProvider } from "react-native-safe-area-context";

import { ErrorBoundary } from "@/components/ErrorBoundary";
import { LockOverlay } from "@/components/LockOverlay";
import { PrivacyConsentGate } from "@/components/PrivacyConsentGate";
import { AccountsProvider } from "@/context/AccountsContext";
import { LockProvider } from "@/context/LockContext";
import { ThemeProvider, useTheme } from "@/context/ThemeContext";

SplashScreen.preventAutoHideAsync();

const queryClient = new QueryClient();

function RootLayoutNav() {
  const { resolvedScheme } = useTheme();
  return (
    <>
      <StatusBar style={resolvedScheme === "dark" ? "light" : "dark"} />
      <Stack screenOptions={{ headerShown: false }}>
        <Stack.Screen name="index" options={{ headerShown: false }} />
        <Stack.Screen
          name="add"
          options={{ headerShown: false, presentation: "modal" }}
        />
        <Stack.Screen
          name="scan"
          options={{ headerShown: false, presentation: "fullScreenModal" }}
        />
        <Stack.Screen
          name="backup"
          options={{ headerShown: false, presentation: "modal" }}
        />
        <Stack.Screen
          name="qr-transfer"
          options={{ headerShown: false, presentation: "fullScreenModal" }}
        />
        <Stack.Screen
          name="edit/[id]"
          options={{ headerShown: false, presentation: "modal" }}
        />
      </Stack>
      <LockOverlay />
    </>
  );
}

export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts({
    Inter_400Regular,
    Inter_500Medium,
    Inter_600SemiBold,
    Inter_700Bold,
  });

  useEffect(() => {
    if (fontsLoaded || fontError) {
      SplashScreen.hideAsync();
    }
  }, [fontsLoaded, fontError]);

  if (!fontsLoaded && !fontError) return null;

  return (
    <SafeAreaProvider>
      <ErrorBoundary>
        <ThemeProvider>
          <PrivacyConsentGate>
            <LockProvider>
              <QueryClientProvider client={queryClient}>
                <GestureHandlerRootView style={{ flex: 1 }}>
                  <KeyboardProvider>
                    <AccountsProvider>
                      <RootLayoutNav />
                    </AccountsProvider>
                  </KeyboardProvider>
                </GestureHandlerRootView>
              </QueryClientProvider>
            </LockProvider>
          </PrivacyConsentGate>
        </ThemeProvider>
      </ErrorBoundary>
    </SafeAreaProvider>
  );
}
