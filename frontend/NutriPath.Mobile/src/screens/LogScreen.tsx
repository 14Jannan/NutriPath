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
import { colors, typography, spacing, radii } from '@/theme';

const MEAL_TYPES = ['Breakfast', 'Lunch', 'Snack', 'Dinner'];

export function LogScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const [date, setDate] = useState(todayIso());
  const [daily, setDaily] = useState<DailyMeals | null>(null);

  const isToday = date === todayIso();

  const load = useCallback((day: string) => {
    getDailyMeals(day)
      .then(setDaily)
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
    confirmAction('Remove this item?', `${item.foodName} (${item.quantityGrams}g)`, 'Remove', async () => {
      try {
        await deleteMealItem(item.id);
        load(date);
      } catch {
        showAlert('Could not remove item', 'Please try again.');
      }
    });
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

        {MEAL_TYPES.map((mealType) => {
          const group = findMealGroup(mealType);
          return (
            <Card key={mealType} style={{ marginBottom: spacing.sm }}>
              <View style={styles.mealHeader}>
                <Text style={styles.mealTitle}>{mealType}</Text>
                <Text style={styles.mealTotal}>{Math.round(group?.totalCalories ?? 0)} kcal</Text>
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
  mealHeader: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: spacing.xs },
  mealTitle: { ...typography.labelLg, color: colors.onSurface },
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
