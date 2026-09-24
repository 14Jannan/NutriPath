import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { getDailyMeals, DailyMeals } from '@/api/mealsApi';
import { LogStackParamList } from '@/navigation/LogStackNavigator';
import { colors, typography, spacing, radii } from '@/theme';

const MEAL_TYPES = ['Breakfast', 'Lunch', 'Snack', 'Dinner'];

export function LogScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const [daily, setDaily] = useState<DailyMeals | null>(null);

  // useFocusEffect re-runs every time this screen comes back into view —
  // e.g. after logging a food and navigating back. A plain useEffect
  // only runs on first mount, so newly-logged items wouldn't appear
  // without this until you fully left and re-entered the tab.
  useFocusEffect(
    useCallback(() => {
      getDailyMeals()
        .then(setDaily)
        .catch(() => setDaily(null));
    }, [])
  );

  function findMealGroup(mealType: string) {
    return daily?.meals.find((m) => m.mealType === mealType);
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>Today's Log</Text>

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
                </View>
              ))}

              <Pressable
                style={styles.addButton}
                onPress={() => navigation.navigate('FoodSearch', { mealType })}
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
  title: { ...typography.headlineLg, color: colors.onSurface, marginBottom: spacing.sm },
  mealHeader: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: spacing.xs },
  mealTitle: { ...typography.labelLg, color: colors.onSurface },
  mealTotal: { ...typography.labelMd, color: colors.onSurfaceVariant },
  itemRow: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4 },
  itemName: { ...typography.bodyMd, color: colors.onSurface, flex: 1 },
  itemDetail: { ...typography.bodySm, color: colors.onSurfaceVariant },
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
