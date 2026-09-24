import React from 'react';
import { View, Text, StyleSheet, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ProgressBar } from '@/components/ProgressBar';
import { colors, typography, spacing, radii } from '@/theme';

// ---- MOCK DATA ----
// Real values arrive from GET /api/nutrition/weekly-score once Phase 12
// implements the deterministic scoring formula from our Phase 0 design.
const mockScore = {
  overall: 84,
  dailyScores: [78, 82, 85, 88, 80, 84, 84], // Mon..Sun, last position is "Today"
  components: [
    { key: 'calories', label: 'Calories', status: 'Optimal', percent: 92, note: 'On target across daily average allowances', icon: 'check-circle' as const, tone: 'good' as const },
    { key: 'protein', label: 'Protein', status: 'Good', percent: 88, note: 'Met goals 6 of 7 days (eggs, dhal, chickpeas)', icon: 'check-circle' as const, tone: 'good' as const },
    { key: 'fibre', label: 'Fibre', status: 'Excellent', percent: 90, note: 'Averaged 31g / day with staple greens', icon: 'star' as const, tone: 'good' as const },
    { key: 'sugar', label: 'Sugar', status: 'Moderate', percent: 74, note: '2 days elevated beyond limit from evening sweets', icon: 'alert' as const, tone: 'warn' as const },
    { key: 'sodium', label: 'Sodium', status: 'Acceptable', percent: 80, note: 'Generally balanced home curries', icon: 'information' as const, tone: 'neutral' as const },
    { key: 'consistency', label: 'Consistency', status: 'Superb', percent: 95, note: 'Logged all 3 main meals daily', icon: 'fire' as const, tone: 'good' as const },
  ],
  wentWell: 'Fibre and consistency were the strongest areas this week — averaging 31g of fibre daily is well above target.',
  toImprove: 'Sugar ran high on two evenings. Swapping dessert for fresh fruit on those days would likely lift the score by a few points.',
};

const toneColor = { good: colors.primary, warn: colors.amberCaution, neutral: colors.secondary };

export function WeeklyScoreScreen() {
  const maxDaily = Math.max(...mockScore.dailyScores);

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.disclaimerRow}>
          <MaterialCommunityIcons name="information-outline" size={14} color={colors.outline} />
          <Text style={styles.disclaimerText}>General wellness guidance, not medical advice.</Text>
        </View>

        <Card style={styles.scoreCard}>
          <Text style={styles.cardTitle}>Weekly Nutrition Score</Text>
          <Text style={styles.bigScore}>
            {mockScore.overall}
            <Text style={styles.bigScoreOutOf}> / 100</Text>
          </Text>
          <View style={styles.strengthPill}>
            <MaterialCommunityIcons name="star" size={14} color={colors.onSecondaryFixed} />
            <Text style={styles.strengthPillText}>Strong Balance</Text>
          </View>
          <Text style={styles.cardSubtitle}>Based on your last 7 days of verified logs.</Text>

          <View style={styles.barChart}>
            {mockScore.dailyScores.map((value, index) => {
              const isToday = index === mockScore.dailyScores.length - 1;
              return (
                <View key={index} style={styles.barColumn}>
                  <View
                    style={[
                      styles.bar,
                      {
                        height: (value / maxDaily) * 70,
                        backgroundColor: isToday ? colors.primary : colors.surfaceContainer,
                      },
                    ]}
                  />
                  <Text style={styles.barLabel}>{isToday ? 'Today' : value}</Text>
                </View>
              );
            })}
          </View>
          <Text style={styles.footnote}>
            Never a medical verdict — calculated from balanced nutrient distribution.
          </Text>
        </Card>

        <Card style={{ marginTop: spacing.sm }}>
          <View style={styles.gaugeHeader}>
            <Text style={styles.cardTitle}>Nutrient Components</Text>
            <Text style={styles.balancedPill}>Target Met: 5/6</Text>
          </View>
          {mockScore.components.map((c) => (
            <View key={c.key} style={styles.componentRow}>
              <MaterialCommunityIcons
                name={c.icon}
                size={18}
                color={toneColor[c.tone]}
                style={{ marginTop: 2 }}
              />
              <View style={{ flex: 1 }}>
                <View style={styles.componentTitleRow}>
                  <Text style={styles.componentLabel}>{c.label}</Text>
                  <Text style={[styles.componentStatus, { color: toneColor[c.tone] }]}>{c.status}</Text>
                </View>
                <ProgressBar progress={c.percent / 100} color={toneColor[c.tone]} />
                <Text style={styles.componentNote}>{c.note}</Text>
              </View>
            </View>
          ))}
        </Card>

        {/* MOCK: observations are generated from real weekly data in Phase 13 */}
        <View style={styles.aiObservations}>
          <View style={styles.gaugeHeader}>
            <MaterialCommunityIcons name="creation" size={18} color={colors.primary} />
            <Text style={styles.cardTitle}>AI Coach Observations</Text>
          </View>
          <View style={styles.observationRow}>
            <MaterialCommunityIcons name="thumb-up-outline" size={16} color={colors.primary} />
            <Text style={styles.observationText}>{mockScore.wentWell}</Text>
          </View>
          <View style={styles.observationRow}>
            <MaterialCommunityIcons name="lightbulb-outline" size={16} color={colors.amberCaution} />
            <Text style={styles.observationText}>{mockScore.toImprove}</Text>
          </View>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, gap: spacing.sm, paddingBottom: spacing.xl },
  disclaimerRow: { flexDirection: 'row', justifyContent: 'center', gap: 6, marginBottom: spacing.xs },
  disclaimerText: { ...typography.bodySm, color: colors.outline },
  scoreCard: { alignItems: 'center' },
  cardTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  bigScore: { ...typography.displayLg, fontSize: 44, color: colors.primary, marginTop: spacing.xs },
  bigScoreOutOf: { ...typography.headlineMd, fontSize: 18, color: colors.onSurfaceVariant },
  strengthPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: colors.secondaryFixed,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.pill,
    marginTop: spacing.xs,
  },
  strengthPillText: { ...typography.labelSm, color: colors.onSecondaryFixed },
  cardSubtitle: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: 4 },
  barChart: { flexDirection: 'row', gap: 8, alignItems: 'flex-end', marginTop: spacing.md, height: 90 },
  barColumn: { alignItems: 'center', gap: 4 },
  bar: { width: 20, borderRadius: 6 },
  barLabel: { ...typography.labelSm, color: colors.onSurfaceVariant },
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
  aiObservations: { backgroundColor: colors.surfaceContainerLow, padding: spacing.md, borderRadius: radii.lg, gap: spacing.sm },
  observationRow: { flexDirection: 'row', gap: 8, alignItems: 'flex-start' },
  observationText: { ...typography.bodySm, color: colors.onSurfaceVariant, flex: 1 },
});
