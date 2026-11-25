import React, { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useAppStatus } from '@/hooks/useAppStatus';
import { colors, spacing, typography } from '@/theme';

/** The maintenance notice set in the admin portal (System settings). Dismissible for this session. */
export function MaintenanceBanner() {
  const { status } = useAppStatus();
  const insets = useSafeAreaInsets();
  const [dismissed, setDismissed] = useState<string | null>(null);
  const message = status?.maintenanceMessage;
  if (!message || dismissed === message) return null;
  return (
    <View style={[styles.banner, { paddingTop: insets.top + spacing.xs }]} accessibilityRole="alert">
      <MaterialCommunityIcons name="bullhorn-outline" size={18} color={colors.amberCaution} />
      <Text style={styles.text}>{message}</Text>
      <Pressable accessibilityRole="button" accessibilityLabel="Dismiss notice" hitSlop={10} onPress={() => setDismissed(message)}>
        <MaterialCommunityIcons name="close" size={18} color={colors.amberCaution} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    position: 'absolute', top: 0, left: 0, right: 0, zIndex: 50, flexDirection: 'row', alignItems: 'center', gap: spacing.sm,
    backgroundColor: colors.amberSoft, paddingHorizontal: spacing.md, paddingBottom: spacing.sm,
  },
  text: { ...typography.bodySm, color: colors.amberCaution, flex: 1 },
});
