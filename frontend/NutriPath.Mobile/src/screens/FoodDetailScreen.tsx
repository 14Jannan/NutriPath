import React, { useCallback, useRef, useState } from 'react';
import { View, Text, StyleSheet, Pressable, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation, useRoute, RouteProp } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { deleteFood, getFoodById, FoodSearchResult } from '@/api/foodsApi';
import { logMealItem } from '@/api/mealsApi';
import { Button } from '@/components/Button';
import { LogStackParamList } from '@/navigation/LogStackNavigator';
import { colors, typography, spacing, radii } from '@/theme';
import { confirmAction, showAlert } from '@/utils/alert';
import { describeApiError } from '@/api/client';

export function FoodDetailScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const route = useRoute<RouteProp<LogStackParamList, 'FoodDetail'>>();
  const { foodId, mealType, date } = route.params;

  const [food, setFood] = useState<FoodSearchResult | null>(null);
  const [quantity, setQuantity] = useState(100);
  const [saving, setSaving] = useState(false);

  const portionChosen = useRef(false);

  // Loads on every focus, so coming back from editing shows the new values.
  useFocusEffect(
    useCallback(() => {
      let active = true;
      getFoodById(foodId)
        .then((data) => {
          if (!active) return;
          setFood(data);
          // Default to one standard serving, but keep a portion the user
          // already picked if they're returning from an edit.
          if (!portionChosen.current) setQuantity(data.servingSizeGrams);
          portionChosen.current = true;
        })
        .catch((error) => {
          if (!active) return;
          showAlert('Could not load food', describeApiError(error));
          navigation.goBack();
        });
      return () => {
        active = false;
      };
    }, [foodId, navigation])
  );

  if (!food) {
    return (
      <SafeAreaView style={styles.screen}>
        <ActivityIndicator style={{ marginTop: 40 }} color={colors.primary} />
      </SafeAreaView>
    );
  }

  // Live-scaled preview: recalculated on every render as `quantity` changes,
  // so the number on screen always matches the current portion size —
  // this is the exact same scaling math as the backend's LogItemAsync,
  // just computed here for instant visual feedback before saving.
  const scale = food.servingSizeGrams > 0 ? quantity / food.servingSizeGrams : 0;
  const scaled = {
    calories: Math.round(food.calories * scale),
    protein: Math.round(food.proteinGrams * scale * 10) / 10,
    carbs: Math.round(food.carbsGrams * scale * 10) / 10,
    fat: Math.round(food.fatGrams * scale * 10) / 10,
    fiber: Math.round(food.fiberGrams * scale * 10) / 10,
  };

  function handleDelete() {
    confirmAction(
      'Delete this food?',
      `"${food!.name}" will be removed from your foods.`,
      'Delete',
      async () => {
        try {
          await deleteFood(food!.id);
          showAlert('Food deleted', undefined, 'success');
          navigation.goBack();
        } catch (error) {
          showAlert("Couldn't delete the food", describeApiError(error));
        }
      },
      true
    );
  }

  async function handleAdd() {
    if (saving) return; // ignore double taps while the first request is in flight
    setSaving(true);
    try {
      await logMealItem(food!.id, quantity, mealType, date);
      showAlert('Added to ' + mealType, `${food!.name} · ${quantity}g`, 'success');
      navigation.navigate('LogHome');
    } catch (error) {
      showAlert('Could not add food', describeApiError(error));
    } finally {
      setSaving(false);
    }
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.header}>
        <Pressable onPress={() => navigation.goBack()} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Meal Detail</Text>
      </View>

      <View style={styles.content}>
        <Text style={styles.foodName}>{food.name}</Text>
        <Text style={styles.foodSource}>Source: {food.sourceName}</Text>

        <View style={styles.portionRow}>
          <Text style={styles.portionLabel}>Portion size</Text>
          <View style={styles.stepper}>
            <Pressable
              style={styles.stepperButton}
              onPress={() => setQuantity((q) => Math.max(10, q - 10))}
            >
              <MaterialCommunityIcons name="minus" size={18} color={colors.primary} />
            </Pressable>
            <Text style={styles.stepperValue}>{quantity}g</Text>
            <Pressable style={styles.stepperButton} onPress={() => setQuantity((q) => q + 10)}>
              <MaterialCommunityIcons name="plus" size={18} color={colors.primary} />
            </Pressable>
          </View>
        </View>

        <View style={styles.kcalBox}>
          <Text style={styles.kcalNumber}>{scaled.calories}</Text>
          <Text style={styles.kcalLabel}>kcal for this portion</Text>
        </View>

        <View style={styles.macroGrid}>
          {[
            { label: 'Protein', value: `${scaled.protein}g` },
            { label: 'Carbs', value: `${scaled.carbs}g` },
            { label: 'Fat', value: `${scaled.fat}g` },
            { label: 'Fibre', value: `${scaled.fiber}g` },
          ].map((m) => (
            <View key={m.label} style={styles.macroCell}>
              <Text style={styles.macroValue}>{m.value}</Text>
              <Text style={styles.macroLabel}>{m.label}</Text>
            </View>
          ))}
        </View>

        <Button
          label={saving ? 'Adding...' : `Add to ${mealType} · ${scaled.calories} kcal`}
          onPress={handleAdd}
          style={{ marginTop: spacing.lg }}
        />

        {food.isCustom && (
          <View style={styles.ownActions}>
            <Pressable
              style={styles.ownAction}
              onPress={() => navigation.navigate('AddFood', { mealType, date, foodId: food.id })}
              accessibilityRole="button"
            >
              <MaterialCommunityIcons name="pencil-outline" size={16} color={colors.primary} />
              <Text style={styles.editText}>Edit this food</Text>
            </Pressable>
            <Pressable style={styles.ownAction} onPress={handleDelete} accessibilityRole="button">
              <MaterialCommunityIcons name="trash-can-outline" size={16} color={colors.amberCaution} />
              <Text style={styles.deleteText}>Delete this food</Text>
            </Pressable>
          </View>
        )}
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.margin, height: 56 },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  content: { padding: spacing.margin },
  foodName: { ...typography.headlineMd, color: colors.onSurface },
  foodSource: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2, marginBottom: spacing.md },
  portionRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: spacing.md },
  portionLabel: { ...typography.labelLg, color: colors.onSurface },
  stepper: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  stepperButton: {
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: colors.surfaceContainer,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stepperValue: { ...typography.labelLg, color: colors.onSurface, minWidth: 50, textAlign: 'center' },
  kcalBox: { alignItems: 'center', paddingVertical: spacing.md },
  kcalNumber: { ...typography.displayLg, fontSize: 36, color: colors.primary },
  kcalLabel: { ...typography.labelMd, color: colors.onSurfaceVariant },
  macroGrid: { flexDirection: 'row', justifyContent: 'space-around' },
  macroCell: { alignItems: 'center' },
  macroValue: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  ownActions: { flexDirection: 'row', justifyContent: 'center', gap: spacing.lg, marginTop: spacing.sm },
  ownAction: { flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: 44 },
  editText: { ...typography.labelMd, color: colors.primary },
  deleteText: { ...typography.labelMd, color: colors.amberCaution },
  macroLabel: { ...typography.labelSm, color: colors.onSurfaceVariant },
});
