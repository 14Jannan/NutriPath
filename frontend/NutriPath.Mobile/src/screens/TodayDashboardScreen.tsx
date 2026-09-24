import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ProgressBar } from '@/components/ProgressBar';
import { CircularProgress } from '@/components/CircularProgress';
import { colors, typography, spacing, radii } from '@/theme';
import * as nutritionApi from '@/api/nutritionApi';
import type { DailyNutrition } from '@/api/nutritionApi';
import { getDailyMeals, type MealGroup } from '@/api/mealsApi';
import { getMyProfile, type ProfileResponse } from '@/api/profileApi';

function greetingForNow() {
  const hour = new Date().getHours();
  if (hour < 12) return 'Good morning';
  if (hour < 17) return 'Good afternoon';
  return 'Good evening';
}

// A target of 0 means it hasn't been set yet — show no progress rather
// than dividing by zero.
function ratio(current: number, target: number) {
  return target > 0 ? current / target : 0;
}

const round = (value: number) => Math.round(value);

const MACRO_ICONS: Record<string, keyof typeof MaterialCommunityIcons.glyphMap> = {
  Protein: 'egg-outline',
  Carbs: 'barley',
  Fat: 'water-outline',
  Fibre: 'leaf',
};

export function TodayDashboardScreen() {
  const [profile, setProfile] = useState<ProfileResponse | null>(null);
  const [nutrition, setNutrition] = useState<DailyNutrition | null>(null);
  const [meals, setMeals] = useState<MealGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  // Refetch every time the tab comes into focus, so a meal logged on
  // another screen shows up here straight away.
  useFocusEffect(
    useCallback(() => {
      let active = true;
      Promise.all([getMyProfile(), nutritionApi.getDailyNutrition(), getDailyMeals()])
        .then(([p, n, m]) => {
          if (!active) return;
          setProfile(p);
          setNutrition(n);
          setMeals(m.meals);
          setError(false);
        })
        .catch(() => active && setError(true))
        .finally(() => active && setLoading(false));
      return () => {
        active = false;
      };
    }, [])
  );

  const today = new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' });
  const firstName = profile?.fullName?.trim().split(' ')[0];

  // Every number here is calculated by the backend (NutritionService);
  // this screen only displays it.
  const targetCalories = nutrition?.targetCalories ?? 0;
  const eatenCalories = nutrition?.eatenCalories ?? 0;
  const remaining = Math.max(0, nutrition?.remainingCalories ?? 0);

  const macros = (nutrition?.macros ?? []).map((m) => ({
    key: m.label,
    label: m.label,
    icon: MACRO_ICONS[m.label] ?? ('circle-outline' as const),
    current: m.current,
    target: m.target,
  }));

  const mealRows = nutritionApi.MEAL_TYPES.map((label) => {
    const meal = meals.find((m) => m.mealType === label);
    return { label, meal };
  });
  const loggedCount = mealRows.filter((row) => row.meal).length;

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.header}>
          <View>
            <Text style={styles.greeting}>
              {greetingForNow()}
              {firstName ? `, ${firstName}` : ''}
            </Text>
            <Text style={styles.date}>{today}</Text>
          </View>
        </View>

        {loading && <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />}

        {!loading && error && (
          <Card>
            <Text style={styles.emptyText}>Couldn't load today's data. Check that the backend is running.</Text>
          </Card>
        )}

        {!loading && !error && (
          <>
            {targetCalories === 0 && (
              <View style={styles.notice}>
                <MaterialCommunityIcons name="information-outline" size={16} color={colors.secondary} />
                <Text style={styles.noticeText}>
                  Your daily targets aren't set yet. Set up your goals in the Profile tab to see them.
                </Text>
              </View>
            )}

            <Card style={styles.gaugeCard}>
              <View style={styles.gaugeHeader}>
                <Text style={styles.cardTitle}>Daily Fuel Gauge</Text>
                <Text style={styles.cardSubtitle}>
                  {targetCalories > 0 ? `Target: ${targetCalories.toLocaleString()} kcal` : 'No target set'}
                </Text>
              </View>
              <View style={styles.gaugeRow}>
                <CircularProgress progress={Math.min(1, ratio(eatenCalories, targetCalories))} size={150} strokeWidth={14}>
                  <Text style={styles.gaugeNumber}>
                    {targetCalories > 0 ? remaining.toLocaleString() : eatenCalories.toLocaleString()}
                  </Text>
                  <Text style={styles.gaugeUnit}>{targetCalories > 0 ? 'kcal left' : 'kcal eaten'}</Text>
                </CircularProgress>
                <View style={styles.gaugeStats}>
                  <View>
                    <Text style={styles.gaugeStatLabel}>Eaten</Text>
                    <Text style={styles.gaugeStatValue}>{eatenCalories.toLocaleString()}</Text>
                    <Text style={styles.gaugeStatUnit}>kcal</Text>
                  </View>
                </View>
              </View>
            </Card>

            <Card style={{ marginTop: spacing.sm }}>
              <View style={styles.gaugeHeader}>
                <Text style={styles.cardTitle}>Nutritional Balance</Text>
              </View>
              {macros.map((macro) => (
                <View key={macro.key} style={styles.macroRow}>
                  <MaterialCommunityIcons name={macro.icon} size={18} color={colors.secondary} />
                  <Text style={styles.macroLabel}>{macro.label}</Text>
                  <View style={{ flex: 1 }}>
                    <ProgressBar progress={Math.min(1, ratio(macro.current, macro.target))} />
                  </View>
                  <Text style={styles.macroValue}>
                    {round(macro.current)}g{macro.target > 0 ? ` / ${macro.target}g` : ''}
                  </Text>
                </View>
              ))}
            </Card>

            <View style={styles.mealsHeader}>
              <Text style={styles.cardTitle}>Today's Meals</Text>
              <Text style={styles.cardSubtitle}>
                {loggedCount} of {mealRows.length} logged
              </Text>
            </View>

            {mealRows.map(({ label, meal }) => (
              <Card key={label} style={{ marginBottom: spacing.sm }}>
                <View style={styles.mealRow}>
                  <View style={{ flex: 1 }}>
                    <View style={styles.mealTitleRow}>
                      <Text style={styles.mealTitle}>{label}</Text>
                      {meal && <MaterialCommunityIcons name="check-circle" size={16} color={colors.primary} />}
                    </View>
                    <Text style={styles.mealTag}>
                      {meal
                        ? `${round(meal.totalCalories)} kcal · ${meal.items.map((i) => i.foodName).join(', ')}`
                        : 'Not logged yet'}
                    </Text>
                  </View>
                </View>
              </Card>
            ))}
          </>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, gap: spacing.sm, paddingBottom: spacing.xl },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' },
  greeting: { ...typography.headlineMd, color: colors.onSurface },
  date: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  notice: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: colors.surfaceContainerLow,
    padding: spacing.sm,
    borderRadius: radii.md,
  },
  noticeText: { ...typography.bodySm, color: colors.onSurfaceVariant, flex: 1 },
  emptyText: { ...typography.bodyMd, color: colors.onSurfaceVariant },
  gaugeCard: { alignItems: 'stretch' },
  gaugeHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: spacing.sm },
  cardTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  cardSubtitle: { ...typography.labelSm, color: colors.onSurfaceVariant },
  gaugeRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-around' },
  gaugeNumber: { ...typography.headlineLg, fontSize: 30, color: colors.onSurface },
  gaugeUnit: { ...typography.labelSm, color: colors.onSurfaceVariant },
  gaugeStats: { gap: spacing.sm },
  gaugeStatLabel: { ...typography.labelSm, color: colors.onSurfaceVariant },
  gaugeStatValue: { ...typography.headlineMd, fontSize: 20, color: colors.onSurface },
  gaugeStatUnit: { ...typography.labelSm, color: colors.onSurfaceVariant },
  macroRow: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: spacing.xs },
  macroLabel: { ...typography.labelMd, color: colors.onSurface, width: 56 },
  macroValue: { ...typography.labelSm, color: colors.onSurfaceVariant, width: 78, textAlign: 'right' },
  mealsHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: spacing.xs },
  mealRow: { flexDirection: 'row', alignItems: 'center' },
  mealTitleRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  mealTitle: { ...typography.labelLg, color: colors.onSurface },
  mealTag: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
});
