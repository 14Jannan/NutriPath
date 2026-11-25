import React, { useState } from 'react';
import { Image, Linking, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/Button';
import { useAuth } from '@/context/AuthContext';
import { colors, radii, spacing, typography } from '@/theme';

export const ADMIN_MESSAGE =
  'You are using an administrator account. Please access the NutriPath Admin Portal to securely manage the application.';

/**
 * The portal address comes only from build configuration, never from the server or a link,
 * so this screen can't be used to send an administrator somewhere else. HTTPS is required,
 * except for local development hosts in development builds.
 */
export function adminPortalUrl(raw = process.env.EXPO_PUBLIC_ADMIN_PORTAL_URL, dev = __DEV__): string | null {
  if (!raw) return null;
  try {
    const url = new URL(raw);
    const local = /^(localhost|127\.0\.0\.1|10\.0\.2\.2|192\.168\.\d+\.\d+|10\.\d+\.\d+\.\d+)$/.test(url.hostname);
    if (url.protocol === 'https:' || (dev && url.protocol === 'http:' && local)) return url.toString();
  } catch {
    // Not a URL: treated as not configured.
  }
  return null;
}

export function AdminAccessScreen() {
  const { logoutUser } = useAuth();
  const [error, setError] = useState<string | null>(null);
  const portal = adminPortalUrl();

  return (
    <SafeAreaView style={styles.safe}>
      <View style={styles.container}>
        <Image source={require('../../assets/icon.png')} style={styles.logo} accessibilityIgnoresInvertColors accessibilityLabel="NutriPath" />
        <View style={styles.shield}>
          <MaterialCommunityIcons name="shield-account" size={36} color={colors.primary} />
        </View>
        <Text style={styles.title} accessibilityRole="header">Administrator Access</Text>
        <Text style={styles.body}>{ADMIN_MESSAGE}</Text>
        {!portal && (
          <Text style={styles.note}>{"The admin portal address isn't configured in this app. Ask your NutriPath system administrator for it."}</Text>
        )}
        {error && <Text style={styles.error} accessibilityLiveRegion="polite">{error}</Text>}
        <Button
          label="Open Admin Portal"
          disabled={!portal}
          onPress={() => {
            if (!portal) return;
            setError(null);
            Linking.openURL(portal).catch(() => setError("Couldn't open the browser. Open the admin portal on a computer instead."));
          }}
          style={styles.button}
        />
        <Button label="Sign out" variant="secondary" onPress={() => void logoutUser()} style={styles.button} />
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.surface },
  container: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.margin, gap: spacing.sm },
  logo: { width: 64, height: 64, borderRadius: radii.md, marginBottom: spacing.md },
  shield: {
    width: 72, height: 72, borderRadius: radii.pill, backgroundColor: colors.secondaryFixed,
    alignItems: 'center', justifyContent: 'center', marginBottom: spacing.sm,
  },
  title: { ...typography.headlineLg, color: colors.onSurface, textAlign: 'center' },
  body: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', maxWidth: 360 },
  note: { ...typography.bodySm, color: colors.amberCaution, textAlign: 'center', maxWidth: 360, marginTop: spacing.xs },
  error: { ...typography.bodySm, color: colors.error, textAlign: 'center', maxWidth: 360 },
  button: { width: '100%', maxWidth: 320, marginTop: spacing.sm },
});
