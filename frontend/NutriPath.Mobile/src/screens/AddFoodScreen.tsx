import React, { useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useNavigation, useRoute, RouteProp } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/Button';
import { Card } from '@/components/Card';
import { createFood } from '@/api/foodsApi';
import { describeApiError } from '@/api/client';
import { LogStackParamList } from '@/navigation/LogStackNavigator';
import { showAlert } from '@/utils/alert';
import { colors, radii, spacing, typography } from '@/theme';

interface FieldProps {
  label: string;
  unit: string;
  value: string;
  onChange: (v: string) => void;
  optional?: boolean;
}

function NumberField({ label, unit, value, onChange, optional }: FieldProps) {
  return (
    <View style={styles.field}>
      <Text style={styles.fieldLabel}>
        {label}
        {optional ? <Text style={styles.optional}> (optional)</Text> : null}
      </Text>
      <View style={styles.inputRow}>
        <TextInput
          style={styles.numberInput}
          value={value}
          onChangeText={(t) => onChange(t.replace(/[^0-9.]/g, ''))}
          keyboardType="decimal-pad"
          placeholder="0"
          placeholderTextColor={colors.outline}
          accessibilityLabel={label}
          maxLength={6}
        />
        <Text style={styles.unit}>{unit}</Text>
      </View>
    </View>
  );
}

/**
 * Adds a private food the catalog doesn't have, e.g. a local dish. Values
 * are per 100 g, like most package labels. The server checks they're
 * plausible; the food is only ever visible to the user who adds it.
 */
export function AddFoodScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const { params } = useRoute<RouteProp<LogStackParamList, 'AddFood'>>();

  const [name, setName] = useState(params.name ?? '');
  const [calories, setCalories] = useState('');
  const [protein, setProtein] = useState('');
  const [carbs, setCarbs] = useState('');
  const [fat, setFat] = useState('');
  const [fiber, setFiber] = useState('');
  const [sugar, setSugar] = useState('');
  const [sodium, setSodium] = useState('');
  const [saving, setSaving] = useState(false);

  const num = (v: string) => (v.trim() === '' ? undefined : Number(v));
  const required = [calories, protein, carbs, fat];
  const complete = name.trim().length >= 2 && required.every((v) => v.trim() !== '' && Number.isFinite(Number(v)));

  // Live consistency check, the same rule the server applies: calories
  // come from protein and carbs (4 kcal/g) and fat (9 kcal/g).
  const fromMacros =
    protein && carbs && fat ? Math.round(4 * (Number(protein) + Number(carbs)) + 9 * Number(fat)) : null;
  const mismatch =
    fromMacros !== null && calories !== ''
      ? Math.abs(Number(calories) - fromMacros) > Math.max(40, fromMacros * 0.35)
      : false;

  async function save() {
    if (!complete || saving) return;
    setSaving(true);
    try {
      const food = await createFood({
        name: name.trim(),
        calories: Number(calories),
        proteinGrams: Number(protein),
        carbsGrams: Number(carbs),
        fatGrams: Number(fat),
        fiberGrams: num(fiber),
        sugarGrams: num(sugar),
        sodiumMilligrams: num(sodium),
      });
      showAlert('Food added', `${food.name} is saved to your foods.`, 'success');
      // Straight to choosing the portion, so it can be logged right away.
      navigation.replace('FoodDetail', { foodId: food.id, mealType: params.mealType, date: params.date });
    } catch (error) {
      showAlert("Couldn't add the food", describeApiError(error));
      setSaving(false);
    }
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable onPress={() => navigation.goBack()} style={styles.backButton} accessibilityLabel="Back">
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Add your own food</Text>
      </View>

      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <View style={styles.notice}>
            <MaterialCommunityIcons name="lock-outline" size={18} color={colors.primary} />
            <Text style={styles.noticeText}>
              Only you can see this food. Use values from a package label or a trusted source, per 100 g.
            </Text>
          </View>

          <Card style={styles.card}>
            <Text style={styles.fieldLabel}>Food name</Text>
            <TextInput
              style={styles.nameInput}
              value={name}
              onChangeText={setName}
              placeholder="e.g. Chicken kottu"
              placeholderTextColor={colors.outline}
              accessibilityLabel="Food name"
              maxLength={80}
            />

            <Text style={styles.sectionTitle}>Per 100 g</Text>
            <View style={styles.grid}>
              <NumberField label="Calories" unit="kcal" value={calories} onChange={setCalories} />
              <NumberField label="Protein" unit="g" value={protein} onChange={setProtein} />
              <NumberField label="Carbs" unit="g" value={carbs} onChange={setCarbs} />
              <NumberField label="Fat" unit="g" value={fat} onChange={setFat} />
              <NumberField label="Fibre" unit="g" value={fiber} onChange={setFiber} optional />
              <NumberField label="Sugar" unit="g" value={sugar} onChange={setSugar} optional />
              <NumberField label="Sodium" unit="mg" value={sodium} onChange={setSodium} optional />
            </View>

            {fromMacros !== null && (
              <Text style={[styles.check, mismatch && styles.checkWarn]}>
                {mismatch
                  ? `Protein, carbs and fat add up to about ${fromMacros} kcal, which doesn't match. Please check.`
                  : `Protein, carbs and fat add up to about ${fromMacros} kcal.`}
              </Text>
            )}
          </Card>
        </ScrollView>

        <View style={styles.footer}>
          <Button
            label={saving ? 'Saving...' : 'Save and choose portion'}
            onPress={save}
            disabled={!complete || saving || mismatch}
          />
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const MAX_WIDTH = 560;

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.margin, height: 56 },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  content: { padding: spacing.margin, paddingTop: 0, width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
  notice: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: colors.surfaceContainerLow,
    padding: spacing.sm,
    borderRadius: radii.md,
    marginBottom: spacing.sm,
  },
  noticeText: { ...typography.bodySm, color: colors.onSurface, flex: 1 },
  card: { gap: spacing.xs },
  sectionTitle: { ...typography.labelLg, color: colors.onSurface, marginTop: spacing.md },
  nameInput: {
    ...typography.bodyMd,
    color: colors.onSurface,
    backgroundColor: colors.surfaceContainerLow,
    borderRadius: radii.md,
    paddingHorizontal: spacing.sm,
    minHeight: 44,
  },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  // Two columns on a phone.
  field: { flexBasis: '46%', flexGrow: 1 },
  fieldLabel: { ...typography.labelMd, color: colors.onSurface, marginBottom: 4 },
  optional: { ...typography.labelSm, color: colors.outline },
  inputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.surfaceContainerLow,
    borderRadius: radii.md,
    paddingHorizontal: spacing.sm,
    minHeight: 44,
  },
  numberInput: { flex: 1, ...typography.bodyMd, color: colors.onSurface },
  unit: { ...typography.labelMd, color: colors.onSurfaceVariant },
  check: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: spacing.sm },
  checkWarn: { color: colors.amberCaution },
  footer: {
    paddingHorizontal: spacing.margin,
    paddingVertical: spacing.sm,
    width: '100%',
    maxWidth: MAX_WIDTH,
    alignSelf: 'center',
  },
});
