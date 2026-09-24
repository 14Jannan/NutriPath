import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { useAuth } from '@/context/AuthContext';
import { getMyProfile, ProfileResponse } from '@/api/profileApi';
import { ProfileStackParamList } from '@/navigation/ProfileStackNavigator';
import { colors, typography, spacing, radii } from '@/theme';

export function ProfileScreen() {
  const { logoutUser } = useAuth();
  const navigation = useNavigation<NativeStackNavigationProp<ProfileStackParamList>>();
  const [profile, setProfile] = useState<ProfileResponse | null>(null);
  const [loading, setLoading] = useState(true);

  // Refetch on focus so targets update straight after saving goals.
  useFocusEffect(
    useCallback(() => {
      let active = true;
      getMyProfile()
        .then((p) => active && setProfile(p))
        .catch(() => active && setProfile(null))
        .finally(() => active && setLoading(false));
      return () => {
        active = false;
      };
    }, [])
  );

  const hasGoals = !!profile && profile.targetCalories > 0;

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>Profile</Text>

        {loading ? (
          <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />
        ) : (
          <>
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

            {profile && (
              <Card style={{ marginTop: spacing.sm }}>
                {hasGoals ? (
                  <>
                    <Text style={styles.sectionTitle}>Daily Targets</Text>
                    <Text style={styles.targetLine}>{profile.targetCalories.toLocaleString()} kcal</Text>
                    <Text style={styles.targetSub}>
                      Protein {profile.targetProteinGrams}g · Carbs {profile.targetCarbsGrams}g · Fat{' '}
                      {profile.targetFatGrams}g · Fibre {profile.targetFiberGrams}g
                    </Text>
                    {profile.allergies.length > 0 && (
                      <Text style={styles.targetSub}>Allergies: {profile.allergies.join(', ')}</Text>
                    )}
                  </>
                ) : (
                  <Text style={styles.sectionNote}>
                    No goals set yet — set them up to unlock your personal daily targets.
                  </Text>
                )}
                <Button
                  label={hasGoals ? 'Edit Goals' : 'Set Up Goals'}
                  variant="secondary"
                  onPress={() => navigation.navigate('GoalsSetup')}
                  style={{ marginTop: spacing.sm }}
                />
              </Card>
            )}
          </>
        )}

        <Button label="Log Out" variant="secondary" onPress={logoutUser} style={{ marginTop: spacing.lg }} />
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, paddingBottom: spacing.xl },
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
  sectionTitle: { ...typography.labelLg, color: colors.onSurface },
  targetLine: { ...typography.headlineMd, color: colors.primary, marginTop: spacing.xs },
  targetSub: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  sectionNote: { ...typography.bodySm, color: colors.onSurfaceVariant, textAlign: 'center' },
});
