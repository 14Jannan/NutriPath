import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { deleteMealItem, getDailyMeals, DailyMeals, MealItemResponse } from '@/api/mealsApi';
import { LogStackParamList } from '@/navigation/LogStackNavigator';
import { addDaysIso, describeDay, todayIso } from '@/utils/date';
import { confirmAction, showAlert } from '@/utils/alert';
import { describeApiError } from '@/api/client';
import { MEAL_WINDOWS, MealStatus, describeWindow, mealStatus } from '@/utils/mealWindows';
import { syncMealReminders } from '@/notifications/mealReminders';
import { colors, typography, spacing, radii } from '@/theme';

// Status chip per meal. Logging is allowed any time: ahead of the window
// when the user knows what they'll eat, or after it when logging late.
const STATUS: Record<MealStatus, { label: string; color: string; background: string }> = {
  logged: { label: 'Logged ✓', color: colors.primary, background: colors.secondaryFixed },
  open: { label: 'Now', color: colors.onPrimary, background: colors.emerald },
  upcoming: { label: 'Upcoming', color: colors.onSurfaceVariant, background: colors.surfaceContainer },
  missed: { label: 'Not logged, add it late', color: colors.amberCaution, background: colors.amberSoft },
};

export function LogScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const [date, setDate] = useState(todayIso());
  const [daily, setDaily] = useState<DailyMeals | null>(null);

  const isToday = date === todayIso();

  const load = useCallback((day: string) => {
    getDailyMeals(day)
      .then((meals) => {
        setDaily(meals);
        // Logging or removing food changes which reminders are still due.
        if (day === todayIso())
          void syncMealReminders(meals.meals.filter((m) => m.items.length > 0).map((m) => m.mealType));
      })
      .catch(() => setDaily(null));
  }, []);

  // useFocusEffect re-runs every time this screen comes back into view —
  // e.g. after logging a food and navigating back. A plain useEffect
  // only runs on first mount, so newly-logged items wouldn't appear
  // without this until you fully left and re-entered the tab.
  useFocusEffect(
    useCallback(() => {
      load(date);
    }, [load, date])
  );

  function changeDay(days: number) {
    const next = addDaysIso(date, days);
    if (next > todayIso()) return; // no logging into the future
    setDaily(null);
    setDate(next);
  }

  function handleDelete(item: MealItemResponse) {
    confirmAction(
      'Remove this item?',
      `${item.foodName} (${item.quantityGrams}g)`,
      'Remove',
      async () => {
        try {
          await deleteMealItem(item.id);
          showAlert('Item removed', item.foodName, 'success');
          load(date);
        } catch (error) {
          showAlert('Could not remove item', describeApiError(error));
        }
      },
      true
    );
  }

  function findMealGroup(mealType: string) {
    return daily?.meals.find((m) => m.mealType === mealType);
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.dayRow}>
          <Pressable onPress={() => changeDay(-1)} style={styles.dayButton} accessibilityLabel="Previous day">
            <MaterialCommunityIcons name="chevron-left" size={24} color={colors.onSurface} />
          </Pressable>
          <View style={{ alignItems: 'center' }}>
            <Text style={styles.title}>{describeDay(date)}</Text>
            <Text style={styles.dayTotal}>{Math.round(daily?.totalCalories ?? 0)} kcal logged</Text>
          </View>
          <Pressable
            onPress={() => changeDay(1)}
            style={[styles.dayButton, isToday && { opacity: 0.3 }]}
            disabled={isToday}
            accessibilityLabel="Next day"
          >
            <MaterialCommunityIcons name="chevron-right" size={24} color={colors.onSurface} />
          </Pressable>
        </View>

        {MEAL_WINDOWS.map((window) => {
          const { mealType } = window;
          const group = findMealGroup(mealType);
          const status = STATUS[mealStatus(window, date, !!group?.items.length)];
          return (
            <Card key={mealType} style={{ marginBottom: spacing.sm }}>
              <View style={styles.mealHeader}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.mealTitle}>
                    {window.emoji} {mealType}
                  </Text>
                  <Text style={styles.mealWindow}>{describeWindow(window)}</Text>
                </View>
                <View style={{ alignItems: 'flex-end', gap: 4 }}>
                  <Text style={[styles.statusChip, { color: status.color, backgroundColor: status.background }]}>
                    {status.label}
                  </Text>
                  <Text style={styles.mealTotal}>{Math.round(group?.totalCalories ?? 0)} kcal</Text>
                </View>
              </View>

              {group?.items.map((item) => (
                <View key={item.id} style={styles.itemRow}>
                  <Text style={styles.itemName}>{item.foodName}</Text>
                  <Text style={styles.itemDetail}>
                    {item.quantityGrams}g · {Math.round(item.calories)} kcal
                  </Text>
                  <Pressable
                    onPress={() => handleDelete(item)}
                    style={styles.deleteButton}
                    hitSlop={8}
                    accessibilityLabel={`Remove ${item.foodName}`}
                  >
                    <MaterialCommunityIcons name="close" size={16} color={colors.outline} />
                  </Pressable>
                </View>
              ))}

              <Pressable
                style={styles.addButton}
                onPress={() => navigation.navigate('FoodSearch', { mealType, date })}
              >
                <MaterialCommunityIcons name="plus" size={16} color={colors.primary} />
                <Text style={styles.addButtonText}>Add food to {mealType}</Text>
              </Pressable>
            </Card>
          );
        })}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { padding: spacing.margin, paddingBottom: spacing.xl },
  dayRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: spacing.sm },
  dayButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  title: { ...typography.headlineLg, color: colors.onSurface },
  dayTotal: { ...typography.labelMd, color: colors.onSurfaceVariant },
  mealHeader: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.sm, marginBottom: spacing.xs },
  mealTitle: { ...typography.labelLg, color: colors.onSurface },
  mealWindow: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: 2 },
  statusChip: {
    ...typography.labelSm,
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: radii.pill,
    overflow: 'hidden',
  },
  mealTotal: { ...typography.labelMd, color: colors.onSurfaceVariant },
  itemRow: { flexDirection: 'row', alignItems: 'center', paddingVertical: 4, gap: spacing.xs },
  itemName: { ...typography.bodyMd, color: colors.onSurface, flex: 1 },
  itemDetail: { ...typography.bodySm, color: colors.onSurfaceVariant },
  deleteButton: { padding: 4 },
  addButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    marginTop: spacing.xs,
    paddingVertical: 8,
    borderRadius: radii.md,
    backgroundColor: colors.surfaceContainerLow,
    justifyContent: 'center',
  },
  addButtonText: { ...typography.labelMd, color: colors.primary },
});
