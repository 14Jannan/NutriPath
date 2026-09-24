import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TextInput, Pressable, Alert, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useNavigation } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { isAxiosError } from 'axios';
import { Button } from '@/components/Button';
import { Card } from '@/components/Card';
import { getMyProfile, updateGoals } from '@/api/profileApi';
import { colors, typography, spacing, radii } from '@/theme';

const SEXES = ['Male', 'Female'];
const ACTIVITY_LEVELS = ['Sedentary', 'Light', 'Moderate', 'VeryActive'];
const ACTIVITY_LABELS: Record<string, string> = { VeryActive: 'Very active' };
const GOALS = ['Lose', 'Maintain', 'Gain'];

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

// Comma-separated text <-> list, e.g. "peanuts, shellfish".
const toList = (text: string) =>
  text
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);

export function GoalsSetupScreen() {
  const navigation = useNavigation();
  const [age, setAge] = useState('');
  const [sex, setSex] = useState('Male');
  const [heightCm, setHeightCm] = useState('');
  const [weightKg, setWeightKg] = useState('');
  const [activityLevel, setActivityLevel] = useState('Moderate');
  const [goal, setGoal] = useState('Maintain');
  const [allergies, setAllergies] = useState('');
  const [preferences, setPreferences] = useState('');
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
        setAllergies(p.allergies.join(', '));
        setPreferences(p.dietaryPreferences.join(', '));
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  async function handleSave() {
    if (saving) return;
    setSaving(true);
    try {
      await updateGoals({
        age: parseInt(age, 10) || 0,
        sex,
        heightCm: parseFloat(heightCm) || 0,
        weightKg: parseFloat(weightKg) || 0,
        activityLevel,
        goal,
        allergies: toList(allergies),
        dietaryPreferences: toList(preferences),
      });
      navigation.goBack();
    } catch (error) {
      // The backend explains exactly which value is out of range.
      const message = isAxiosError(error) ? error.response?.data?.message : undefined;
      Alert.alert('Could not save', message ?? 'Please check your entries and try again.');
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

            <Text style={styles.label}>Weight (kg)</Text>
            <TextInput
              style={styles.input}
              keyboardType="decimal-pad"
              value={weightKg}
              onChangeText={setWeightKg}
              placeholder="e.g. 65"
              placeholderTextColor={colors.outline}
            />

            <Text style={styles.label}>Activity level</Text>
            <SegmentedRow
              options={ACTIVITY_LEVELS}
              value={activityLevel}
              onChange={setActivityLevel}
              labels={ACTIVITY_LABELS}
            />

            <Text style={styles.label}>Goal</Text>
            <SegmentedRow options={GOALS} value={goal} onChange={setGoal} />

            <Text style={styles.label}>Allergies (comma-separated)</Text>
            <TextInput
              style={styles.input}
              value={allergies}
              onChangeText={setAllergies}
              placeholder="e.g. peanuts, shellfish"
              placeholderTextColor={colors.outline}
            />

            <Text style={styles.label}>Dietary preferences (comma-separated)</Text>
            <TextInput
              style={styles.input}
              value={preferences}
              onChangeText={setPreferences}
              placeholder="e.g. vegetarian"
              placeholderTextColor={colors.outline}
            />
          </Card>

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
