import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { apiClient } from '@/api/client';
import { Button } from '@/components/Button';
import { useAuth } from '@/context/AuthContext';
import { colors, typography, spacing } from '@/theme';

export function HomeScreen() {
  const { logoutUser } = useAuth();
  const [profile, setProfile] = useState<{ email: string; fullName: string } | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    apiClient
      .get('/api/profile/me')
      .then((response) => setProfile(response.data))
      .finally(() => setLoading(false));
  }, []);

  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.content}>
        <Text style={styles.title}>You're logged in 🎉</Text>
        {loading ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <Text style={styles.body}>
            {profile ? `Signed in as ${profile.email}` : 'Could not load profile.'}
          </Text>
        )}
        <Text style={styles.note}>
          This confirms the full chain: token stored securely, attached automatically to
          requests, and accepted by a protected backend endpoint.
        </Text>
        <Button label="Log Out" onPress={logoutUser} style={{ marginTop: spacing.lg }} />
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { flex: 1, padding: spacing.margin, justifyContent: 'center', gap: spacing.sm },
  title: { ...typography.headlineLg, color: colors.onSurface, textAlign: 'center' },
  body: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center' },
  note: { ...typography.bodySm, color: colors.outline, textAlign: 'center', marginTop: spacing.md },
});
