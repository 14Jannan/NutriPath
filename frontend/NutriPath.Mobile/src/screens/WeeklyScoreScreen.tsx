import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ProgressBar } from '@/components/ProgressBar';
import { colors, typography, spacing, radii } from '@/theme';
import * as nutritionApi from '@/api/nutritionApi';
import type { WeeklyScore } from '@/api/nutritionApi';

// What each component measures — fixed descriptions of the backend's
// WeeklyScoreService formula, not per-user claims.
const COMPONENT_NOTES: Record<string, string> = {
  Calories: '7-day average compared with your calorie target',
  Protein: '7-day average compared with your protein target',
  Fiber: '7-day average compared with your fibre target',
  Sugar: 'Stays at 100 while the daily average is under 50g',
  Sodium: 'Stays at 100 while the daily average is under 2,300mg',
  Consistency: 'Share of the last 7 days with at least one meal logged',
};

const COMPONENT_LABELS: Record<string, string> = { Fiber: 'Fibre' };

function describe(percent: number) {
  if (percent >= 90) return { status: 'On target', icon: 'check-circle' as const, color: colors.primary };
  if (percent >= 60) return { status: 'Fair', icon: 'information' as const, color: colors.secondary };
  return { status: 'Needs attention', icon: 'alert' as const, color: colors.amberCaution };
}

export function WeeklyScoreScreen() {
  const [score, setScore] = useState<WeeklyScore | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useFocusEffect(
    useCallback(() => {
      let active = true;
      nutritionApi
        .getWeeklyScore()
        .then((result) => {
          if (!active) return;
          setScore(result);
          setError(false);
        })
        .catch(() => active && setError(true))
        .finally(() => active && setLoading(false));
      return () => {
        active = false;
      };
    }, [])
  );

  const targetsMet = score?.components.filter((c) => c.percent >= 90).length ?? 0;

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.disclaimerRow}>
          <MaterialCommunityIcons name="information-outline" size={14} color={colors.outline} />
          <Text style={styles.disclaimerText}>General wellness guidance, not medical advice.</Text>
        </View>

        {loading && <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />}

        {!loading && (error || !score) && (
          <Card>
            <Text style={styles.emptyText}>Couldn't load your weekly score. Check that the backend is running.</Text>
          </Card>
        )}

        {!loading && score && (
          <>
            <Card style={styles.scoreCard}>
              <Text style={styles.cardTitle}>Weekly Nutrition Score</Text>
              <Text style={styles.bigScore}>
                {score.overall}
                <Text style={styles.bigScoreOutOf}> / 100</Text>
              </Text>
              <Text style={styles.cardSubtitle}>Based on your last 7 days of logged meals.</Text>
              <Text style={styles.footnote}>
                Never a medical verdict — calculated from balanced nutrient distribution.
              </Text>
            </Card>

            <Card style={{ marginTop: spacing.sm }}>
              <View style={styles.gaugeHeader}>
                <Text style={styles.cardTitle}>Nutrient Components</Text>
                <Text style={styles.balancedPill}>
                  Target Met: {targetsMet}/{score.components.length}
                </Text>
              </View>
              {score.components.map((c) => {
                const look = describe(c.percent);
                return (
                  <View key={c.name} style={styles.componentRow}>
                    <MaterialCommunityIcons name={look.icon} size={18} color={look.color} style={{ marginTop: 2 }} />
                    <View style={{ flex: 1 }}>
                      <View style={styles.componentTitleRow}>
                        <Text style={styles.componentLabel}>{COMPONENT_LABELS[c.name] ?? c.name}</Text>
                        <Text style={[styles.componentStatus, { color: look.color }]}>
                          {c.percent}% · {look.status}
                        </Text>
                      </View>
                      <ProgressBar progress={c.percent / 100} color={look.color} />
                      {COMPONENT_NOTES[c.name] && <Text style={styles.componentNote}>{COMPONENT_NOTES[c.name]}</Text>}
                    </View>
                  </View>
                );
              })}
            </Card>
          </>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, gap: spacing.sm, paddingBottom: spacing.xl },
  disclaimerRow: { flexDirection: 'row', justifyContent: 'center', gap: 6, marginBottom: spacing.xs },
  disclaimerText: { ...typography.bodySm, color: colors.outline },
  emptyText: { ...typography.bodyMd, color: colors.onSurfaceVariant },
  scoreCard: { alignItems: 'center' },
  cardTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  bigScore: { ...typography.displayLg, fontSize: 44, color: colors.primary, marginTop: spacing.xs },
  bigScoreOutOf: { ...typography.headlineMd, fontSize: 18, color: colors.onSurfaceVariant },
  cardSubtitle: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: 4 },
  footnote: { ...typography.bodySm, color: colors.outline, textAlign: 'center', marginTop: spacing.sm },
  gaugeHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: spacing.sm },
  balancedPill: {
    ...typography.labelSm,
    color: colors.primary,
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.pill,
  },
  componentRow: { flexDirection: 'row', gap: spacing.sm, marginBottom: spacing.sm },
  componentTitleRow: { flexDirection: 'row', justifyContent: 'space-between' },
  componentLabel: { ...typography.labelMd, color: colors.onSurface },
  componentStatus: { ...typography.labelSm, fontWeight: '600' },
  componentNote: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
});
