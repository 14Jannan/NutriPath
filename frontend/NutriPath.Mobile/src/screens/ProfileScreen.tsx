import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ActivityIndicator, ScrollView, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Avatar } from '@/components/Avatar';
import { AvatarPickerSheet } from '@/components/AvatarPickerSheet';
import { describeApiError } from '@/api/client';
import { showAlert } from '@/utils/alert';
import { useAuth } from '@/context/AuthContext';
import { getMyProfile, ProfileResponse, updateAvatar } from '@/api/profileApi';
import { getSyncStatus, DataSourceStatus } from '@/api/syncApi';
import { ProfileStackParamList } from '@/navigation/ProfileStackNavigator';
import { colors, typography, spacing, radii } from '@/theme';

function describeSync(days: number | null) {
  if (days === null) return 'never synced';
  if (days === 0) return 'synced today';
  return `synced ${days} day${days === 1 ? '' : 's'} ago`;
}

export function ProfileScreen() {
  const { logoutUser } = useAuth();
  const navigation = useNavigation<NativeStackNavigationProp<ProfileStackParamList>>();
  const [profile, setProfile] = useState<ProfileResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [sources, setSources] = useState<DataSourceStatus[]>([]);
  const [avatarOpen, setAvatarOpen] = useState(false);
  const [savingAvatar, setSavingAvatar] = useState(false);

  async function saveAvatar(avatarId: string) {
    setSavingAvatar(true);
    try {
      setProfile(await updateAvatar(avatarId));
      setAvatarOpen(false);
      showAlert('Avatar updated', undefined, 'success');
    } catch (error) {
      showAlert("Couldn't change your avatar", describeApiError(error));
    } finally {
      setSavingAvatar(false);
    }
  }

  // Refetch on focus so targets update straight after saving goals.
  useFocusEffect(
    useCallback(() => {
      let active = true;
      getMyProfile()
        .then((p) => active && setProfile(p))
        .catch(() => active && setProfile(null))
        .finally(() => active && setLoading(false));
      // Freshness is informational only, so a failure just hides the card.
      getSyncStatus()
        .then((s) => active && setSources(s))
        .catch(() => active && setSources([]));
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
              <Pressable
                onPress={() => setAvatarOpen(true)}
                style={({ pressed }) => [styles.avatarButton, pressed && { opacity: 0.85 }]}
                accessibilityRole="button"
                accessibilityLabel="Change avatar"
              >
                <Avatar avatarId={profile?.avatarId} size={88} />
                <View style={styles.avatarEdit}>
                  <MaterialCommunityIcons name="pencil" size={14} color={colors.onPrimary} />
                </View>
              </Pressable>
              <Pressable onPress={() => setAvatarOpen(true)} hitSlop={6}>
                <Text style={styles.changeAvatar}>Change avatar</Text>
              </Pressable>
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

        {sources.length > 0 && (
          <Card style={{ marginTop: spacing.sm }}>
            <Text style={styles.sectionTitle}>Food Data</Text>
            {sources.map((s) => (
              <View key={s.name} style={styles.sourceRow}>
                <Text style={styles.sourceName}>{s.name}</Text>
                <Text style={styles.targetSub}>
                  {s.foodCount.toLocaleString()} foods · {describeSync(s.daysSinceSync)}
                </Text>
              </View>
            ))}
          </Card>
        )}

        <Button label="Log Out" variant="secondary" onPress={logoutUser} style={{ marginTop: spacing.lg }} />
      </ScrollView>

      <AvatarPickerSheet
        visible={avatarOpen}
        current={profile?.avatarId ?? null}
        saving={savingAvatar}
        onClose={() => setAvatarOpen(false)}
        onSave={saveAvatar}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, paddingBottom: spacing.xl },
  title: { ...typography.headlineLg, color: colors.onSurface, marginBottom: spacing.md },
  profileCard: { alignItems: 'center' },
  avatarButton: { marginBottom: spacing.xs },
  avatarEdit: {
    position: 'absolute',
    right: 0,
    bottom: 0,
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: colors.primary,
    borderWidth: 2,
    borderColor: colors.surfaceContainerLowest,
    alignItems: 'center',
    justifyContent: 'center',
  },
  changeAvatar: { ...typography.labelMd, color: colors.primary, marginBottom: spacing.sm },
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
  sourceRow: { marginTop: spacing.xs },
  sourceName: { ...typography.labelMd, color: colors.onSurface },
  sectionNote: { ...typography.bodySm, color: colors.onSurfaceVariant, textAlign: 'center' },
});
