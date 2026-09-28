import React, { useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { getNetworkStateAsync, useNetworkState, type NetworkState } from 'expo-network';
import { Button } from '@/components/Button';
import { colors, spacing, typography } from '@/theme';

/**
 * Offline only when the phone says so for certain: no connection, or a
 * connection (e.g. Wi-Fi without internet) that can't reach the internet.
 * Undefined means "not known yet", which never blocks the app.
 */
export function isOffline(state: NetworkState): boolean {
  return state.isConnected === false || state.isInternetReachable === false;
}

/**
 * Covers the whole app while there's no internet, since every screen needs
 * the backend. The app stays mounted underneath, so nothing is lost, and
 * this disappears on its own as soon as the connection is back.
 */
export function OfflineGate() {
  const network = useNetworkState();
  const [checking, setChecking] = useState(false);
  const [stillOffline, setStillOffline] = useState(false);

  if (!isOffline(network)) return null;

  async function retry() {
    setChecking(true);
    try {
      setStillOffline(isOffline(await getNetworkStateAsync()));
    } finally {
      setChecking(false);
    }
  }

  return (
    <View style={styles.overlay} accessibilityViewIsModal>
      <SafeAreaView style={styles.content}>
        <View style={styles.iconCircle}>
          <MaterialCommunityIcons name="wifi-off" size={44} color={colors.primary} />
        </View>
        <Text style={styles.title}>No internet connection</Text>
        <Text style={styles.body}>
          NutriPath needs the internet to load your meals, scores and assistant. Please connect to Wi-Fi or turn on
          mobile data.
        </Text>
        <Text style={styles.hint}>
          {network.isConnected
            ? "You're connected, but it has no internet. Try another Wi-Fi network or mobile data."
            : 'Your meal reminders still work while you’re offline.'}
        </Text>

        {checking ? (
          <ActivityIndicator color={colors.primary} style={styles.button} />
        ) : (
          <Button label="Try Again" onPress={retry} style={styles.button} />
        )}
        {stillOffline && !checking && <Text style={styles.stillOffline}>Still offline. Check your connection.</Text>}
      </SafeAreaView>
    </View>
  );
}

const styles = StyleSheet.create({
  // Above every screen and toast, and catches all touches.
  overlay: { position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, backgroundColor: colors.surface, zIndex: 1000, elevation: 1000 },
  content: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg },
  iconCircle: {
    width: 88,
    height: 88,
    borderRadius: 44,
    backgroundColor: colors.secondaryFixed,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.md,
  },
  title: { ...typography.headlineMd, color: colors.onSurface, textAlign: 'center' },
  body: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: spacing.sm, maxWidth: 360 },
  hint: { ...typography.bodySm, color: colors.outline, textAlign: 'center', marginTop: spacing.sm, maxWidth: 360 },
  button: { marginTop: spacing.lg, maxWidth: 320 },
  stillOffline: { ...typography.labelMd, color: colors.amberCaution, marginTop: spacing.sm },
});
