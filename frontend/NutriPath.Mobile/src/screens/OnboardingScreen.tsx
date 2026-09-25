import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Animated,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/Button';
import { Card } from '@/components/Card';
import { ProgressBar } from '@/components/ProgressBar';
import { Avatar } from '@/components/Avatar';
import { AvatarPicker } from '@/components/AvatarPicker';
import { getAvatar } from '@/constants/avatars';
import { NONE_OPTION, OTHER_OPTION, combineKnownAndOther } from '@/components/MultiSelectDropdown';
import {
  ACTIVITY_OPTIONS,
  ALLERGY_OPTIONS,
  ChoiceOption,
  DIET_OPTIONS,
  GOAL_OPTIONS,
  SEX_OPTIONS,
} from '@/constants/profileOptions';
import { updateAvatar, updateGoals, UpdateGoalsPayload } from '@/api/profileApi';
import { describeApiError } from '@/api/client';
import { useAuth } from '@/context/AuthContext';
import { useLiveGoalsAnalysis } from '@/hooks/useLiveGoalsAnalysis';
import { showAlert } from '@/utils/alert';
import { bmi, bmiCategory, healthyWeightRange, heightHint, LIMITS, validateBody } from '@/utils/bodyMetrics';
import { colors, radii, spacing, typography } from '@/theme';

const STEPS = ['welcome', 'avatar', 'about', 'body', 'activity', 'goal', 'food', 'summary'] as const;
type Step = (typeof STEPS)[number];

// ---------------------------------------------------------------------------
// Small building blocks
// ---------------------------------------------------------------------------

/** A big tappable option card with an icon, used for sex/activity/goal. */
function ChoiceCard({
  option,
  selected,
  onPress,
  compact = false,
}: {
  option: ChoiceOption;
  selected: boolean;
  onPress: () => void;
  compact?: boolean;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ selected }}
      accessibilityLabel={option.label}
      style={({ pressed }) => [
        styles.choice,
        compact && styles.choiceCompact,
        selected && styles.choiceSelected,
        pressed && styles.pressed,
      ]}
    >
      <View style={[styles.choiceIcon, selected && styles.choiceIconSelected]}>
        <MaterialCommunityIcons name={option.icon} size={compact ? 26 : 24} color={selected ? colors.onPrimary : colors.primary} />
      </View>
      <View style={compact ? { alignItems: 'center' } : { flex: 1 }}>
        <Text style={[styles.choiceLabel, compact && { textAlign: 'center' }]}>{option.label}</Text>
        {!compact && option.description && <Text style={styles.choiceDescription}>{option.description}</Text>}
      </View>
      {!compact && (
        <MaterialCommunityIcons
          name={selected ? 'check-circle' : 'circle-outline'}
          size={22}
          color={selected ? colors.primary : colors.outline}
        />
      )}
    </Pressable>
  );
}

/** A large number with -/+ buttons either side; the number is also typeable. */
function NumberStepper({
  value,
  onChange,
  unit,
  step,
  min,
  max,
  start,
  label,
}: {
  value: string;
  onChange: (v: string) => void;
  unit: string;
  step: number;
  min: number;
  max: number;
  // Where the first +/- tap starts from when the field is still empty.
  start: number;
  label: string;
}) {
  function bump(direction: 1 | -1) {
    const current = parseFloat(value);
    const next = Number.isFinite(current) ? current + direction * step : start;
    const clamped = Math.min(max, Math.max(min, next));
    onChange(String(Math.round(clamped * 10) / 10));
  }

  return (
    <View style={styles.stepper}>
      <Pressable style={styles.stepperButton} onPress={() => bump(-1)} accessibilityLabel={`Decrease ${label}`} hitSlop={6}>
        <MaterialCommunityIcons name="minus" size={26} color={colors.primary} />
      </Pressable>
      <View style={styles.stepperValue}>
        <TextInput
          style={styles.stepperInput}
          value={value}
          onChangeText={(t) => onChange(t.replace(/[^0-9.]/g, ''))}
          keyboardType="decimal-pad"
          placeholder="–"
          placeholderTextColor={colors.outline}
          accessibilityLabel={label}
          maxLength={5}
        />
        <Text style={styles.stepperUnit}>{unit}</Text>
      </View>
      <Pressable style={styles.stepperButton} onPress={() => bump(1)} accessibilityLabel={`Increase ${label}`} hitSlop={6}>
        <MaterialCommunityIcons name="plus" size={26} color={colors.primary} />
      </Pressable>
    </View>
  );
}

/** Tappable chips; "None" is exclusive and "Other" reveals a text box. */
function ChipSelect({
  options,
  selected,
  onChange,
  otherText,
  onOtherTextChange,
  otherPlaceholder,
}: {
  options: readonly string[];
  selected: string[];
  onChange: (s: string[]) => void;
  otherText: string;
  onOtherTextChange: (t: string) => void;
  otherPlaceholder: string;
}) {
  function toggle(option: string) {
    if (selected.includes(option)) onChange(selected.filter((s) => s !== option));
    else if (option === NONE_OPTION) {
      onChange([NONE_OPTION]);
      onOtherTextChange('');
    } else onChange([...selected.filter((s) => s !== NONE_OPTION), option]);
  }

  return (
    <View>
      <View style={styles.chipWrap}>
        {options.map((option) => {
          const on = selected.includes(option);
          return (
            <Pressable
              key={option}
              onPress={() => toggle(option)}
              accessibilityRole="checkbox"
              accessibilityState={{ checked: on }}
              style={({ pressed }) => [styles.chip, on && styles.chipOn, pressed && styles.pressed]}
            >
              {on && <MaterialCommunityIcons name="check" size={14} color={colors.onPrimary} />}
              <Text style={[styles.chipText, on && styles.chipTextOn]}>{option}</Text>
            </Pressable>
          );
        })}
      </View>
      {selected.includes(OTHER_OPTION) && (
        <TextInput
          style={styles.otherInput}
          value={otherText}
          onChangeText={onOtherTextChange}
          placeholder={otherPlaceholder}
          placeholderTextColor={colors.outline}
          autoFocus
        />
      )}
    </View>
  );
}

function StepTitle({ icon, title, subtitle }: { icon: ChoiceOption['icon']; title: string; subtitle: string }) {
  return (
    <View style={styles.stepTitleBlock}>
      <View style={styles.stepIcon}>
        <MaterialCommunityIcons name={icon} size={28} color={colors.primary} />
      </View>
      <Text style={styles.stepTitle}>{title}</Text>
      <Text style={styles.stepSubtitle}>{subtitle}</Text>
    </View>
  );
}

function SummaryRow({ label, value, onEdit }: { label: string; value: string; onEdit: () => void }) {
  return (
    <Pressable style={styles.summaryRow} onPress={onEdit} accessibilityLabel={`Edit ${label}`}>
      <Text style={styles.summaryLabel}>{label}</Text>
      <Text style={styles.summaryValue} numberOfLines={2}>
        {value}
      </Text>
      <MaterialCommunityIcons name="pencil-outline" size={16} color={colors.outline} />
    </Pressable>
  );
}

// ---------------------------------------------------------------------------
// The wizard
// ---------------------------------------------------------------------------

/**
 * Required first-login setup. Shown instead of the main app until the
 * profile is saved, one friendly question per step, ending with the
 * user's live targets and a personalised AI analysis.
 */
export function OnboardingScreen() {
  const { fullName, logoutUser, markProfileComplete } = useAuth();
  const firstName = fullName.trim().split(' ')[0];

  const [stepIndex, setStepIndex] = useState(0);
  const [avatarId, setAvatarId] = useState<string | null>(null);
  const [sex, setSex] = useState('');
  const [age, setAge] = useState('');
  const [heightCm, setHeightCm] = useState('');
  const [weightKg, setWeightKg] = useState('');
  const [activityLevel, setActivityLevel] = useState('');
  const [goal, setGoal] = useState('');
  const [allergies, setAllergies] = useState<string[]>([]);
  const [otherAllergies, setOtherAllergies] = useState('');
  const [preferences, setPreferences] = useState<string[]>([]);
  const [otherPreferences, setOtherPreferences] = useState('');
  const [saving, setSaving] = useState(false);

  const step: Step = STEPS[stepIndex];
  const scrollRef = useRef<ScrollView>(null);

  // Each step slides and fades in.
  const enter = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    enter.setValue(0);
    Animated.timing(enter, { toValue: 1, duration: 260, useNativeDriver: true }).start();
    scrollRef.current?.scrollTo({ y: 0, animated: false });
  }, [stepIndex, enter]);

  const ageNum = parseInt(age, 10);
  const heightNum = parseFloat(heightCm);
  const weightNum = parseFloat(weightKg);
  const ageValid = ageNum >= LIMITS.age.min && ageNum <= LIMITS.age.max;
  const bodyError = heightCm && weightKg ? validateBody(ageValid ? ageNum : 30, heightNum, weightNum) : null;
  const heightValid = heightNum >= LIMITS.heightCm.min && heightNum <= LIMITS.heightCm.max;
  const bmiValue = heightValid && weightKg && !bodyError ? bmi(heightNum, weightNum) : null;
  const range = heightValid ? healthyWeightRange(heightNum) : null;

  const allergyList = combineKnownAndOther(allergies, otherAllergies);
  const preferenceList = combineKnownAndOther(preferences, otherPreferences);
  const foodAnswered = (list: string[], other: string) =>
    list.length > 0 && (!list.includes(OTHER_OPTION) || other.trim().length > 0);

  // Whether "Continue" is allowed on each step: every question is required.
  const stepValid: Record<Step, boolean> = {
    welcome: true,
    avatar: !!avatarId,
    about: !!sex && ageValid,
    body: !!heightCm && !!weightKg && validateBody(ageNum, heightNum, weightNum) === null,
    activity: !!activityLevel,
    goal: !!goal,
    food: foodAnswered(allergies, otherAllergies) && foodAnswered(preferences, otherPreferences),
    summary: true,
  };

  // The live analysis only runs on the summary step, so no AI calls are
  // made while the user is still answering questions.
  const payload = useMemo<UpdateGoalsPayload | null>(
    () =>
      step === 'summary'
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
    // Lists compared by content, not identity, to avoid re-running every render.
    [step, ageNum, sex, heightNum, weightNum, activityLevel, goal, allergyList.join('|'), preferenceList.join('|')]
  );
  const { preview, insight, insightLoading, insightError } = useLiveGoalsAnalysis(payload);

  function next() {
    if (stepValid[step] && stepIndex < STEPS.length - 1) setStepIndex(stepIndex + 1);
  }
  function back() {
    if (stepIndex > 0) setStepIndex(stepIndex - 1);
  }
  const goTo = (s: Step) => setStepIndex(STEPS.indexOf(s));

  async function finish() {
    if (!payload || saving) return;
    setSaving(true);
    try {
      if (avatarId) await updateAvatar(avatarId);
      await updateGoals(payload);
      showAlert(firstName ? `You're all set, ${firstName}!` : "You're all set!", 'Your personal plan is ready.', 'success');
      markProfileComplete();
    } catch (error) {
      showAlert("Couldn't save your profile", describeApiError(error));
      setSaving(false);
    }
  }

  const labelOf = (options: ChoiceOption[], value: string) => options.find((o) => o.value === value)?.label ?? value;
  const listText = (list: string[]) => (list.length ? list.join(', ') : 'None');
  const lowBmiLosing = goal === 'Lose' && bmiValue !== null && bmiValue < 18.5;

  // Questions are counted without the welcome and summary screens.
  const questionCount = STEPS.length - 2;
  const questionNumber = Math.min(Math.max(stepIndex, 1), questionCount);

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        {/* Header: back, progress, log out */}
        <View style={styles.header}>
          <View style={styles.headerInner}>
            <Pressable
              onPress={back}
              style={[styles.headerButton, stepIndex === 0 && { opacity: 0 }]}
              disabled={stepIndex === 0}
              accessibilityLabel="Back"
            >
              <MaterialCommunityIcons name="arrow-left" size={24} color={colors.onSurface} />
            </Pressable>
            <View style={{ flex: 1 }}>
              {step !== 'welcome' && step !== 'summary' ? (
                <>
                  <Text style={styles.progressText}>
                    Step {questionNumber} of {questionCount}
                  </Text>
                  <ProgressBar progress={questionNumber / questionCount} />
                </>
              ) : (
                <Text style={styles.progressText}>{step === 'welcome' ? 'Welcome' : 'Your plan'}</Text>
              )}
            </View>
            <Pressable onPress={logoutUser} style={styles.headerButton} accessibilityLabel="Log out">
              <Text style={styles.logoutText}>Log out</Text>
            </Pressable>
          </View>
        </View>

        <ScrollView ref={scrollRef} contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <Animated.View
            style={[
              styles.content,
              {
                opacity: enter,
                transform: [{ translateX: enter.interpolate({ inputRange: [0, 1], outputRange: [24, 0] }) }],
              },
            ]}
          >
            {step === 'welcome' && (
              <View style={styles.welcome}>
                <View style={styles.heroIcon}>
                  <MaterialCommunityIcons name="leaf" size={48} color={colors.onPrimary} />
                </View>
                <Text style={styles.heroTitle}>{firstName ? `Hi ${firstName}!` : 'Welcome!'}</Text>
                <Text style={styles.heroSubtitle}>
                  Let's build your personal nutrition plan. It takes about a minute, and every number is calculated
                  for you.
                </Text>
                <Card style={styles.benefits}>
                  {[
                    { icon: 'target' as const, text: 'Daily calorie and macro targets made for your body' },
                    { icon: 'chart-arc' as const, text: 'A weekly score that shows how you are doing' },
                    { icon: 'robot-happy-outline' as const, text: 'AI tips that fit your age, goal and diet' },
                  ].map((b) => (
                    <View key={b.text} style={styles.benefitRow}>
                      <MaterialCommunityIcons name={b.icon} size={22} color={colors.primary} />
                      <Text style={styles.benefitText}>{b.text}</Text>
                    </View>
                  ))}
                </Card>
              </View>
            )}

            {step === 'avatar' && (
              <>
                <StepTitle icon="emoticon-happy-outline" title="Pick your avatar" subtitle="It shows on your profile. You can change it any time." />
                <View style={styles.avatarPreview}>
                  <Avatar avatarId={avatarId} size={96} />
                  <Text style={styles.avatarName}>{getAvatar(avatarId)?.label ?? 'Tap one below'}</Text>
                </View>
                <AvatarPicker selected={avatarId} onSelect={setAvatarId} />
              </>
            )}

            {step === 'about' && (
              <>
                <StepTitle icon="account-outline" title="About you" subtitle="This sets how your body uses energy." />
                <Text style={styles.question}>Sex</Text>
                <View style={styles.row}>
                  {SEX_OPTIONS.map((o) => (
                    <ChoiceCard key={o.value} option={o} selected={sex === o.value} onPress={() => setSex(o.value)} compact />
                  ))}
                </View>
                <Text style={styles.question}>Age</Text>
                <NumberStepper value={age} onChange={setAge} unit="years" step={1} min={LIMITS.age.min} max={LIMITS.age.max} start={20} label="Age" />
                {age !== '' && !ageValid && (
                  <Text style={styles.errorText}>
                    Age must be between {LIMITS.age.min} and {LIMITS.age.max}.
                  </Text>
                )}
              </>
            )}

            {step === 'body' && (
              <>
                <StepTitle icon="human-male-height" title="Your body" subtitle="Measured values give the most accurate targets." />
                <Text style={styles.question}>Height</Text>
                <NumberStepper value={heightCm} onChange={setHeightCm} unit="cm" step={1} min={LIMITS.heightCm.min} max={LIMITS.heightCm.max} start={165} label="Height" />
                <Text style={styles.hint}>{heightHint(ageValid ? ageNum : null)}</Text>

                <Text style={styles.question}>Weight</Text>
                <NumberStepper value={weightKg} onChange={setWeightKg} unit="kg" step={0.5} min={LIMITS.weightKg.min} max={LIMITS.weightKg.max} start={60} label="Weight" />
                {range && (
                  <Text style={styles.hint}>
                    Healthy weight for {heightNum} cm: {range.min}–{range.max} kg
                    {ageValid && ageNum < 18 ? ' (adult range; for under-18s it depends on age)' : ''}
                  </Text>
                )}
                {bmiValue !== null && ageValid && ageNum >= 18 && (
                  <View style={styles.bmiPill}>
                    <MaterialCommunityIcons name="scale-bathroom" size={16} color={colors.primary} />
                    <Text style={styles.bmiText}>
                      BMI {bmiValue.toFixed(1)} · {bmiCategory(bmiValue)}
                    </Text>
                  </View>
                )}
                {bodyError && <Text style={styles.errorText}>{bodyError}</Text>}
              </>
            )}

            {step === 'activity' && (
              <>
                <StepTitle icon="run-fast" title="How active are you?" subtitle="Think about a typical week." />
                <View style={styles.stack}>
                  {ACTIVITY_OPTIONS.map((o) => (
                    <ChoiceCard key={o.value} option={o} selected={activityLevel === o.value} onPress={() => setActivityLevel(o.value)} />
                  ))}
                </View>
              </>
            )}

            {step === 'goal' && (
              <>
                <StepTitle icon="flag-checkered" title="What's your goal?" subtitle="You can change this any time from your profile." />
                <View style={styles.stack}>
                  {GOAL_OPTIONS.map((o) => (
                    <ChoiceCard key={o.value} option={o} selected={goal === o.value} onPress={() => setGoal(o.value)} />
                  ))}
                </View>
                {lowBmiLosing && (
                  <View style={styles.notice}>
                    <MaterialCommunityIcons name="information-outline" size={18} color={colors.amberCaution} />
                    <Text style={styles.noticeText}>
                      Your BMI is below the healthy range, so losing weight isn't usually recommended. Maintain or Gain
                      may suit you better. Talk to a doctor if you're unsure.
                    </Text>
                  </View>
                )}
              </>
            )}

            {step === 'food' && (
              <>
                <StepTitle icon="food-apple-outline" title="Food needs" subtitle="Suggestions will always respect these. Pick None if nothing applies." />
                <Text style={styles.question}>Food allergies</Text>
                <ChipSelect
                  options={ALLERGY_OPTIONS}
                  selected={allergies}
                  onChange={setAllergies}
                  otherText={otherAllergies}
                  onOtherTextChange={setOtherAllergies}
                  otherPlaceholder="Which foods? e.g. kiwi, mustard"
                />
                <Text style={styles.question}>Dietary preferences</Text>
                <ChipSelect
                  options={DIET_OPTIONS}
                  selected={preferences}
                  onChange={setPreferences}
                  otherText={otherPreferences}
                  onOtherTextChange={setOtherPreferences}
                  otherPlaceholder="Please specify, e.g. no seafood"
                />
              </>
            )}

            {step === 'summary' && (
              <>
                <View style={styles.avatarPreview}>
                  <Avatar avatarId={avatarId} size={72} />
                </View>
                <StepTitle icon="clipboard-check-outline" title="Your plan" subtitle="Check your details, then start. Tap any row to change it." />

                <Card style={styles.targetsCard}>
                  <Text style={styles.targetsLabel}>Your daily target</Text>
                  {preview ? (
                    <>
                      <Text style={styles.targetsKcal}>{preview.targetCalories.toLocaleString()} kcal</Text>
                      <View style={styles.macroRow}>
                        {[
                          { label: 'Protein', value: preview.targetProteinGrams },
                          { label: 'Carbs', value: preview.targetCarbsGrams },
                          { label: 'Fat', value: preview.targetFatGrams },
                          { label: 'Fibre', value: preview.targetFiberGrams },
                        ].map((m) => (
                          <View key={m.label} style={styles.macroCell}>
                            <Text style={styles.macroValue}>{m.value}g</Text>
                            <Text style={styles.macroLabel}>{m.label}</Text>
                          </View>
                        ))}
                      </View>
                    </>
                  ) : (
                    <ActivityIndicator color={colors.onPrimary} style={{ marginVertical: spacing.md }} />
                  )}
                </Card>

                <Card style={styles.insightCard}>
                  <View style={styles.insightHeader}>
                    <MaterialCommunityIcons name="robot-happy-outline" size={20} color={colors.primary} />
                    <Text style={styles.insightTitle}>AI analysis for you</Text>
                  </View>
                  {insightLoading ? (
                    <View style={styles.insightHeader}>
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

                <Card style={{ marginTop: spacing.sm, paddingVertical: spacing.xs }}>
                  <SummaryRow label="Avatar" value={getAvatar(avatarId)?.label ?? '-'} onEdit={() => goTo('avatar')} />
                  <SummaryRow label="About you" value={`${labelOf(SEX_OPTIONS, sex)}, ${ageNum} years`} onEdit={() => goTo('about')} />
                  <SummaryRow label="Body" value={`${heightNum} cm · ${weightNum} kg`} onEdit={() => goTo('body')} />
                  <SummaryRow label="Activity" value={labelOf(ACTIVITY_OPTIONS, activityLevel)} onEdit={() => goTo('activity')} />
                  <SummaryRow label="Goal" value={labelOf(GOAL_OPTIONS, goal)} onEdit={() => goTo('goal')} />
                  <SummaryRow label="Allergies" value={listText(allergyList)} onEdit={() => goTo('food')} />
                  <SummaryRow label="Diet" value={listText(preferenceList)} onEdit={() => goTo('food')} />
                </Card>
              </>
            )}
          </Animated.View>
        </ScrollView>

        {/* Sticky footer so the main action is always in thumb reach. */}
        <View style={styles.footer}>
          <View style={styles.footerInner}>
            {step === 'summary' ? (
              <Button label={saving ? 'Saving...' : 'Start my journey'} onPress={finish} disabled={saving || !preview} />
            ) : (
              <Button label={step === 'welcome' ? "Let's get started" : 'Continue'} onPress={next} disabled={!stepValid[step]} />
            )}
          </View>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

// Content is centred and capped in width, so it looks like a phone app on a
// wide browser window and still fills small phone screens.
const MAX_WIDTH = 520;

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: { paddingHorizontal: spacing.md, paddingTop: spacing.xs, paddingBottom: spacing.sm },
  headerInner: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
  headerButton: { minWidth: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
  progressText: { ...typography.labelMd, color: colors.onSurfaceVariant, marginBottom: 6, textAlign: 'center' },
  logoutText: { ...typography.labelMd, color: colors.outline },
  scroll: { flexGrow: 1, paddingHorizontal: spacing.margin, paddingBottom: spacing.lg },
  content: { width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
  pressed: { opacity: 0.85, transform: [{ scale: 0.98 }] },

  welcome: { alignItems: 'center', paddingTop: spacing.lg },
  heroIcon: {
    width: 96,
    height: 96,
    borderRadius: 48,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.md,
  },
  heroTitle: { ...typography.headlineLg, color: colors.onSurface, textAlign: 'center' },
  heroSubtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: spacing.xs, maxWidth: 380 },
  benefits: { width: '100%', marginTop: spacing.lg, gap: spacing.md },
  benefitRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  benefitText: { ...typography.bodyMd, color: colors.onSurface, flex: 1 },

  avatarPreview: { alignItems: 'center', gap: spacing.xs, marginBottom: spacing.md },
  avatarName: { ...typography.labelLg, color: colors.primary },
  stepTitleBlock: { alignItems: 'center', marginBottom: spacing.md, marginTop: spacing.xs },
  stepIcon: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.surfaceContainer,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.sm,
  },
  stepTitle: { ...typography.headlineMd, color: colors.onSurface, textAlign: 'center' },
  stepSubtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: 4 },
  question: { ...typography.labelLg, color: colors.onSurface, marginTop: spacing.md, marginBottom: spacing.xs },

  row: { flexDirection: 'row', gap: spacing.sm },
  stack: { gap: spacing.sm },
  choice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.sm,
    minHeight: 64,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.lg,
    borderWidth: 2,
    borderColor: 'transparent',
  },
  choiceCompact: { flex: 1, flexDirection: 'column', paddingVertical: spacing.md },
  choiceSelected: { borderColor: colors.primary, backgroundColor: colors.surfaceContainerLow },
  choiceIcon: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: colors.surfaceContainer,
    alignItems: 'center',
    justifyContent: 'center',
  },
  choiceIconSelected: { backgroundColor: colors.primary },
  choiceLabel: { ...typography.labelLg, color: colors.onSurface },
  choiceDescription: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },

  stepper: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.lg,
    padding: spacing.xs,
  },
  stepperButton: {
    width: 52,
    height: 52,
    borderRadius: 26,
    backgroundColor: colors.surfaceContainer,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stepperValue: { flexDirection: 'row', alignItems: 'baseline', justifyContent: 'center', flex: 1 },
  stepperInput: {
    ...typography.displayLg,
    fontSize: 36,
    color: colors.onSurface,
    textAlign: 'center',
    minWidth: 90,
    paddingVertical: 0,
  },
  stepperUnit: { ...typography.labelLg, color: colors.onSurfaceVariant, marginLeft: 4 },

  hint: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 6 },
  errorText: { ...typography.bodySm, color: colors.amberCaution, marginTop: 6 },
  bmiPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    alignSelf: 'flex-start',
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radii.pill,
    marginTop: spacing.sm,
  },
  bmiText: { ...typography.labelMd, color: colors.primary },
  notice: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: colors.surfaceContainerLowest,
    borderLeftWidth: 3,
    borderLeftColor: colors.amberCaution,
    padding: spacing.sm,
    borderRadius: radii.md,
    marginTop: spacing.md,
  },
  noticeText: { ...typography.bodySm, color: colors.onSurface, flex: 1 },

  chipWrap: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 14,
    minHeight: 40,
    borderRadius: radii.pill,
    backgroundColor: colors.surfaceContainerLowest,
    borderWidth: 1,
    borderColor: colors.surfaceContainer,
  },
  chipOn: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { ...typography.labelMd, color: colors.onSurface },
  chipTextOn: { color: colors.onPrimary },
  otherInput: {
    ...typography.bodyMd,
    color: colors.onSurface,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.primary,
    paddingHorizontal: spacing.sm,
    minHeight: 44,
    marginTop: spacing.sm,
  },

  targetsCard: { backgroundColor: colors.primary, alignItems: 'center' },
  targetsLabel: { ...typography.labelMd, color: colors.secondaryFixed },
  targetsKcal: { ...typography.displayLg, fontSize: 36, color: colors.onPrimary, marginVertical: 4 },
  macroRow: { flexDirection: 'row', justifyContent: 'space-around', width: '100%', marginTop: spacing.xs },
  macroCell: { alignItems: 'center' },
  macroValue: { ...typography.labelLg, color: colors.onPrimary },
  macroLabel: { ...typography.labelSm, color: colors.secondaryFixed },
  insightCard: { marginTop: spacing.sm, gap: spacing.xs },
  insightHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  insightTitle: { ...typography.labelLg, color: colors.onSurface },
  insightText: { ...typography.bodyMd, color: colors.onSurface },
  disclaimer: { ...typography.labelSm, color: colors.outline, marginTop: spacing.xs },
  summaryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: colors.surfaceContainer,
  },
  summaryLabel: { ...typography.labelMd, color: colors.onSurfaceVariant, width: 76 },
  summaryValue: { ...typography.bodyMd, color: colors.onSurface, flex: 1 },

  footer: {
    paddingHorizontal: spacing.margin,
    paddingTop: spacing.sm,
    paddingBottom: spacing.sm,
    backgroundColor: colors.surface,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.surfaceContainer,
  },
  footerInner: { width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
});
