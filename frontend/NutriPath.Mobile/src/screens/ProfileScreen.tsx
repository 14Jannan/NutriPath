import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { apiClient } from '@/api/client';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { useAuth } from '@/context/AuthContext';
import { colors, typography, spacing, radii } from '@/theme';

interface MyProfile {
  email: string;
  fullName: string;
  emailVerified: boolean;
}

export function ProfileScreen() {
  const { logoutUser } = useAuth();
  const [profile, setProfile] = useState<MyProfile | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    apiClient
      .get<MyProfile>('/api/profile/me')
      .then((response) => setProfile(response.data))
      .catch(() => setProfile(null))
      .finally(() => setLoading(false));
  }, []);

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.content}>
        <Text style={styles.title}>Profile</Text>

        {loading ? (
          <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />
        ) : (
          <Card style={styles.profileCard}>
            <View style={styles.avatar}>
              <MaterialCommunityIcons name="account" size={28} color={colors.onPrimary} />
            </View>
            <Text style={styles.name}>{profile?.fullName || 'NutriPath User'}</Text>
            <Text style={styles.email}>{profile?.email ?? 'Could not load profile.'}</Text>
            {profile?.emailVerified && (
              <View style={styles.verifiedPill}>
                <MaterialCommunityIcons name="check-decagram" size={14} color={colors.primary} />
                <Text style={styles.verifiedText}>Email Verified</Text>
              </View>
            )}
          </Card>
        )}

        <Card style={{ marginTop: spacing.sm }}>
          <Text style={styles.sectionNote}>
            Nutrition goals, allergies, and dietary preferences are added here in Phase 8.
          </Text>
        </Card>

        <Button label="Log Out" variant="secondary" onPress={logoutUser} style={{ marginTop: spacing.lg }} />
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { flex: 1, padding: spacing.margin },
  title: { ...typography.headlineLg, color: colors.onSurface, marginBottom: spacing.md },
  profileCard: { alignItems: 'center' },
  avatar: {
    width: 64,
    height: 64,
    borderRadius: 32,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.sm,
  },
  name: { ...typography.headlineMd, fontSize: 18, color: colors.onSurface },
  email: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  verifiedPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.pill,
    marginTop: spacing.sm,
  },
  verifiedText: { ...typography.labelSm, color: colors.primary },
  sectionNote: { ...typography.bodySm, color: colors.onSurfaceVariant, textAlign: 'center' },
});
