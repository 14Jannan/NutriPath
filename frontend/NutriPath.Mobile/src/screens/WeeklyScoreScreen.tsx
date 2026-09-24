import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ProgressBar } from '@/components/ProgressBar';
import { colors, typography, spacing, radii } from '@/theme';
import * as nutritionApi from '@/api/nutritionApi';
import type { ScoreComponent, WeeklyScore } from '@/api/nutritionApi';

// The status text, note and tone all come from the backend's
// WeeklyScoreService; this only maps the tone to an icon and colour.
const TONE_LOOK: Record<ScoreComponent['tone'], { icon: 'check-circle' | 'information' | 'alert'; color: string }> = {
  good: { icon: 'check-circle', color: colors.primary },
  neutral: { icon: 'information', color: colors.secondary },
  warn: { icon: 'alert', color: colors.amberCaution },
};

// Short weekday labels for the last 7 days, oldest first, ending today —
// the same order as the backend's dailyScores.
function lastSevenDayLabels() {
  return Array.from({ length: 7 }, (_, i) => {
    const d = new Date();
    d.setDate(d.getDate() - (6 - i));
    return d.toLocaleDateString(undefined, { weekday: 'narrow' });
  });
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
  const dayLabels = lastSevenDayLabels();

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
              <Text style={styles.cardTitle}>Last 7 Days</Text>
              <View style={styles.dayRow}>
                {score.dailyScores.map((dayScore, i) => (
                  <View key={i} style={styles.dayColumn}>
                    <View style={styles.dayBarTrack}>
                      <View style={[styles.dayBarFill, { height: `${Math.max(dayScore, 2)}%` }]} />
                    </View>
                    <Text style={styles.dayScore}>{dayScore > 0 ? dayScore : '–'}</Text>
                    <Text style={styles.dayLabel}>{dayLabels[i]}</Text>
                  </View>
                ))}
              </View>
            </Card>

            <Card style={{ marginTop: spacing.sm }}>
              <View style={styles.gaugeHeader}>
                <Text style={styles.cardTitle}>Nutrient Components</Text>
                <Text style={styles.balancedPill}>
                  Target Met: {targetsMet}/{score.components.length}
                </Text>
              </View>
              {score.components.map((c) => {
                const look = TONE_LOOK[c.tone] ?? TONE_LOOK.neutral;
                return (
                  <View key={c.key} style={styles.componentRow}>
                    <MaterialCommunityIcons name={look.icon} size={18} color={look.color} style={{ marginTop: 2 }} />
                    <View style={{ flex: 1 }}>
                      <View style={styles.componentTitleRow}>
                        <Text style={styles.componentLabel}>{c.label}</Text>
                        <Text style={[styles.componentStatus, { color: look.color }]}>
                          {c.percent}% · {c.status}
                        </Text>
                      </View>
                      <ProgressBar progress={c.percent / 100} color={look.color} />
                      <Text style={styles.componentNote}>{c.note}</Text>
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
  dayRow: { flexDirection: 'row', justifyContent: 'space-between', marginTop: spacing.sm },
  dayColumn: { alignItems: 'center', flex: 1 },
  dayBarTrack: {
    width: 14,
    height: 64,
    borderRadius: 7,
    backgroundColor: colors.surfaceContainer,
    justifyContent: 'flex-end',
    overflow: 'hidden',
  },
  dayBarFill: { width: '100%', backgroundColor: colors.primary, borderRadius: 7 },
  dayScore: { ...typography.labelSm, color: colors.onSurface, marginTop: 4 },
  dayLabel: { ...typography.labelSm, color: colors.onSurfaceVariant },
  componentRow: { flexDirection: 'row', gap: spacing.sm, marginBottom: spacing.sm },
  componentTitleRow: { flexDirection: 'row', justifyContent: 'space-between' },
  componentLabel: { ...typography.labelMd, color: colors.onSurface },
  componentStatus: { ...typography.labelSm, fontWeight: '600' },
  componentNote: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
});
