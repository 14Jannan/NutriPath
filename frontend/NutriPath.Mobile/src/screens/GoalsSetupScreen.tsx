import React, { useEffect, useMemo, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TextInput, Pressable, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useNavigation } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { isAxiosError } from 'axios';
import { Button } from '@/components/Button';
import { Card } from '@/components/Card';
import {
  MultiSelectDropdown,
  NONE_OPTION,
  OTHER_OPTION,
  combineKnownAndOther,
  splitKnownAndOther,
} from '@/components/MultiSelectDropdown';
import { getMyProfile, updateGoals, UpdateGoalsPayload } from '@/api/profileApi';
import { useLiveGoalsAnalysis } from '@/hooks/useLiveGoalsAnalysis';
import { colors, typography, spacing, radii } from '@/theme';
import { showAlert } from '@/utils/alert';
import { bmi, bmiCategory, healthyWeightRange, heightHint, LIMITS, validateBody } from '@/utils/bodyMetrics';

const SEXES = ['Male', 'Female', 'Other'];
const ACTIVITY_LEVELS = ['Sedentary', 'Light', 'Moderate', 'VeryActive'];
const ACTIVITY_LABELS: Record<string, string> = { VeryActive: 'Very active' };
const GOALS = ['Lose', 'Maintain', 'Gain'];

// The most common food allergens. Names are kept close to how foods are
// named, since the assistant filters suggestions by matching them.
const ALLERGY_OPTIONS = [
  NONE_OPTION, 'Peanuts', 'Tree nuts', 'Milk', 'Eggs', 'Fish', 'Shellfish',
  'Wheat', 'Gluten', 'Soy', 'Sesame', OTHER_OPTION,
] as const;

const DIET_OPTIONS = [
  NONE_OPTION, 'Vegetarian', 'Vegan', 'Pescatarian', 'Halal', 'No beef', 'No pork',
  'Lactose-free', 'Gluten-free', 'Low sugar', OTHER_OPTION,
] as const;

function SegmentedRow({
  options,
  value,
  onChange,
  labels = {},
}: {
  options: string[];
  value: string;
  onChange: (v: string) => void;
  labels?: Record<string, string>;
}) {
  return (
    <View style={styles.segmentRow}>
      {options.map((opt) => (
        <Pressable
          key={opt}
          style={[styles.segment, value === opt && styles.segmentActive]}
          onPress={() => onChange(opt)}
        >
          <Text style={[styles.segmentText, value === opt && styles.segmentTextActive]}>{labels[opt] ?? opt}</Text>
        </Pressable>
      ))}
    </View>
  );
}

const isSelected = (list: string[], option: string) => list.includes(option);

export function GoalsSetupScreen() {
  const navigation = useNavigation();
  const [age, setAge] = useState('');
  const [sex, setSex] = useState('Male');
  const [heightCm, setHeightCm] = useState('');
  const [weightKg, setWeightKg] = useState('');
  const [activityLevel, setActivityLevel] = useState('Moderate');
  const [goal, setGoal] = useState('Maintain');
  const [allergies, setAllergies] = useState<string[]>([]);
  const [otherAllergies, setOtherAllergies] = useState('');
  const [preferences, setPreferences] = useState<string[]>([]);
  const [otherPreferences, setOtherPreferences] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Pre-fill with the saved profile when editing existing goals.
  useEffect(() => {
    getMyProfile()
      .then((p) => {
        if (p.age > 0) {
          setAge(String(p.age));
          setHeightCm(String(p.heightCm));
          setWeightKg(String(p.weightKg));
          if (SEXES.includes(p.sex)) setSex(p.sex);
          setActivityLevel(p.activityLevel);
          setGoal(p.goal);
        }
        const savedAllergies = splitKnownAndOther(p.allergies, ALLERGY_OPTIONS);
        setAllergies(savedAllergies.selected);
        setOtherAllergies(savedAllergies.otherText);
        const savedPreferences = splitKnownAndOther(p.dietaryPreferences, DIET_OPTIONS);
        setPreferences(savedPreferences.selected);
        setOtherPreferences(savedPreferences.otherText);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  const ageNum = parseInt(age, 10);
  const heightNum = parseFloat(heightCm);
  const weightNum = parseFloat(weightKg);
  const heightValid = heightNum >= LIMITS.heightCm.min && heightNum <= LIMITS.heightCm.max;
  const bodyError = age && heightCm && weightKg ? validateBody(ageNum, heightNum, weightNum) : null;
  const bmiValue = !bodyError && heightValid && weightKg ? bmi(heightNum, weightNum) : null;
  const range = heightValid ? healthyWeightRange(heightNum) : null;

  const allergyList = combineKnownAndOther(allergies, otherAllergies);
  const preferenceList = combineKnownAndOther(preferences, otherPreferences);

  // Only complete, plausible values are analysed; anything else clears the cards.
  const livePayload = useMemo<UpdateGoalsPayload | null>(
    () =>
      validateBody(ageNum, heightNum, weightNum) === null
        ? {
            age: ageNum,
            sex,
            heightCm: heightNum,
            weightKg: weightNum,
            activityLevel,
            goal,
            allergies: allergyList,
            dietaryPreferences: preferenceList,
          }
        : null,
    // The lists are compared by content, not identity, to avoid re-running every render.
    [ageNum, heightNum, weightNum, sex, activityLevel, goal, allergyList.join('|'), preferenceList.join('|')]
  );
  const { preview, insight, insightLoading, insightError } = useLiveGoalsAnalysis(livePayload);

  async function handleSave() {
    if (saving) return;

    // Checked here first for an instant message; the server checks again.
    const problem = validateBody(ageNum, heightNum, weightNum);
    if (problem) {
      showAlert('Please check your details', problem);
      return;
    }
    if (isSelected(allergies, OTHER_OPTION) && !otherAllergies.trim()) {
      showAlert('Please specify', 'You ticked "Other" for allergies. Type them in, or untick Other.');
      return;
    }
    if (isSelected(preferences, OTHER_OPTION) && !otherPreferences.trim()) {
      showAlert('Please specify', 'You ticked "Other" for dietary preferences. Type them in, or untick Other.');
      return;
    }

    setSaving(true);
    try {
      await updateGoals({
        age: ageNum,
        sex,
        heightCm: heightNum,
        weightKg: weightNum,
        activityLevel,
        goal,
        allergies: allergyList,
        dietaryPreferences: preferenceList,
      });
      navigation.goBack();
    } catch (error) {
      // The backend explains exactly which value is out of range.
      const message = isAxiosError(error) ? error.response?.data?.message : undefined;
      showAlert('Could not save', message ?? 'Please check your entries and try again.');
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
        <Text style={styles.headerTitle}>Nutrition Goals</Text>
      </View>

      {loading ? (
        <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />
      ) : (
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.title}>Set your nutrition goals</Text>
          <Text style={styles.subtitle}>
            Your daily calorie and macro targets are calculated from this, using the Mifflin-St Jeor formula.
          </Text>

          <Card style={styles.card}>
            <Text style={styles.label}>Age</Text>
            <TextInput
              style={styles.input}
              keyboardType="number-pad"
              value={age}
              onChangeText={setAge}
              placeholder="e.g. 22"
              placeholderTextColor={colors.outline}
            />

            <Text style={styles.label}>Sex</Text>
            <SegmentedRow options={SEXES} value={sex} onChange={setSex} />

            <Text style={styles.label}>Height (cm)</Text>
            <TextInput
              style={styles.input}
              keyboardType="decimal-pad"
              value={heightCm}
              onChangeText={setHeightCm}
              placeholder="e.g. 170"
              placeholderTextColor={colors.outline}
            />
            <Text style={styles.hint}>{heightHint(Number.isFinite(ageNum) ? ageNum : null)}</Text>

            <Text style={styles.label}>Weight (kg)</Text>
            <TextInput
              style={styles.input}
              keyboardType="decimal-pad"
              value={weightKg}
              onChangeText={setWeightKg}
              placeholder="e.g. 65"
              placeholderTextColor={colors.outline}
            />
            {range && (
              <Text style={styles.hint}>
                Healthy weight for {heightNum} cm: {range.min}–{range.max} kg
                {ageNum < 18 ? ' (adult range; for under-18s it depends on age)' : ''}
              </Text>
            )}
            {bmiValue !== null && ageNum >= 18 && (
              <Text style={[styles.hint, styles.bmiLine]}>
                BMI {bmiValue.toFixed(1)} · {bmiCategory(bmiValue)}
              </Text>
            )}
            {bodyError && <Text style={styles.errorText}>{bodyError}</Text>}

            <Text style={styles.label}>Activity level</Text>
            <SegmentedRow
              options={ACTIVITY_LEVELS}
              value={activityLevel}
              onChange={setActivityLevel}
              labels={ACTIVITY_LABELS}
            />

            <Text style={styles.label}>Goal</Text>
            <SegmentedRow options={GOALS} value={goal} onChange={setGoal} />

            <Text style={styles.label}>Food allergies</Text>
            <MultiSelectDropdown
              options={ALLERGY_OPTIONS}
              selected={allergies}
              onChange={setAllergies}
              otherText={otherAllergies}
              onOtherTextChange={setOtherAllergies}
              placeholder="Select any food allergies"
              otherPlaceholder="Which foods? e.g. kiwi, mustard"
            />

            <Text style={styles.label}>Dietary preferences</Text>
            <MultiSelectDropdown
              options={DIET_OPTIONS}
              selected={preferences}
              onChange={setPreferences}
              otherText={otherPreferences}
              onOtherTextChange={setOtherPreferences}
              placeholder="Select any dietary preferences"
              otherPlaceholder="Please specify, e.g. no seafood"
            />
          </Card>

          {!livePayload && (
            <Text style={styles.liveHint}>
              Fill in your age, height and weight to see your targets and a personalised analysis.
            </Text>
          )}

          {livePayload && (
            <Card style={styles.liveCard}>
              <View style={styles.liveHeader}>
                <MaterialCommunityIcons name="target" size={18} color={colors.primary} />
                <Text style={styles.liveTitle}>Your daily targets</Text>
              </View>
              {preview ? (
                <>
                  <Text style={styles.liveKcal}>{preview.targetCalories.toLocaleString()} kcal</Text>
                  <Text style={styles.hint}>
                    Protein {preview.targetProteinGrams}g · Carbs {preview.targetCarbsGrams}g · Fat{' '}
                    {preview.targetFatGrams}g · Fibre {preview.targetFiberGrams}g
                  </Text>
                </>
              ) : (
                <ActivityIndicator color={colors.primary} style={{ alignSelf: 'flex-start', marginTop: spacing.xs }} />
              )}
            </Card>
          )}

          {livePayload && (
            <Card style={styles.liveCard}>
              <View style={styles.liveHeader}>
                <MaterialCommunityIcons name="robot-happy-outline" size={18} color={colors.primary} />
                <Text style={styles.liveTitle}>AI analysis for you</Text>
              </View>
              {insightLoading ? (
                <View style={styles.liveHeader}>
                  <ActivityIndicator color={colors.primary} />
                  <Text style={styles.hint}>Analysing your age, height, weight and goal...</Text>
                </View>
              ) : insight ? (
                <Text style={styles.insightText}>{insight}</Text>
              ) : (
                insightError && <Text style={styles.hint}>{insightError}</Text>
              )}
              <Text style={styles.disclaimer}>General wellness guidance, not medical advice.</Text>
            </Card>
          )}

          <Button label={saving ? 'Saving...' : 'Save Goals'} onPress={handleSave} style={{ marginTop: spacing.lg }} />
        </ScrollView>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.margin, height: 56 },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  content: { padding: spacing.margin, paddingTop: 0, paddingBottom: spacing.xl },
  title: { ...typography.headlineLg, color: colors.onSurface },
  subtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, marginTop: 4, marginBottom: spacing.md },
  card: { gap: spacing.xs },
  label: { ...typography.labelMd, color: colors.onSurface, marginTop: spacing.sm },
  input: {
    backgroundColor: colors.surfaceContainerLow,
    borderRadius: radii.md,
    paddingHorizontal: spacing.sm,
    height: 44,
    ...typography.bodyMd,
    color: colors.onSurface,
  },
  liveHint: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: spacing.md, textAlign: 'center' },
  liveCard: { marginTop: spacing.sm, gap: spacing.xs },
  liveHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  liveTitle: { ...typography.labelLg, color: colors.onSurface },
  liveKcal: { ...typography.headlineMd, color: colors.primary },
  insightText: { ...typography.bodyMd, color: colors.onSurface },
  disclaimer: { ...typography.labelSm, color: colors.outline, marginTop: spacing.xs },
  hint: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  bmiLine: { color: colors.primary },
  errorText: { ...typography.bodySm, color: colors.amberCaution, marginTop: 2 },
  segmentRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  segment: {
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: radii.pill,
    backgroundColor: colors.surfaceContainerLow,
  },
  segmentActive: { backgroundColor: colors.primary },
  segmentText: { ...typography.labelMd, color: colors.onSurface },
  segmentTextActive: { color: colors.onPrimary },
});
