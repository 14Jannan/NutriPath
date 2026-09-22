import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  Pressable,
  Alert,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { TextField } from '@/components/TextField';
import { Chip } from '@/components/Chip';
import { Button } from '@/components/Button';
import { colors, typography, spacing, radii } from '@/theme';

interface CreateAccountScreenProps {
  onAccountCreated: () => void;
  onGoToLogin: () => void;
  onGoBack: () => void;
}

const DIET_OPTIONS = [
  { key: 'balanced', label: 'Balanced Island Diet', icon: 'rice' as const },
  { key: 'vegetarian', label: 'Vegetarian / Dhal Focus', icon: 'leaf' as const },
  { key: 'protein', label: 'Lean Protein & Fitness', icon: 'dumbbell' as const },
];

export function CreateAccountScreen({ onAccountCreated, onGoToLogin, onGoBack }: CreateAccountScreenProps) {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [dietFocus, setDietFocus] = useState('balanced');
  const [agreedToTerms, setAgreedToTerms] = useState(false);

  const passwordIsStrong = password.length >= 8 && /[0-9!@#$%^&*]/.test(password);

  function handleSubmit() {
    if (!fullName.trim() || !email.trim() || !password) {
      Alert.alert('Missing information', 'Please fill in your name, email, and password.');
      return;
    }
    if (!agreedToTerms) {
      Alert.alert('Terms required', 'Please agree to the Terms of Service and Privacy Policy.');
      return;
    }
    // Real registration call arrives in Phase 5 — for now this proves the
    // form, validation, and navigation flow all work end to end.
    Alert.alert('Account details captured', `Welcome, ${fullName}! (Backend wiring comes in Phase 5.)`);
    onAccountCreated();
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable onPress={onGoBack} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Auth Onboarding · Step 1</Text>
      </View>

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={{ flex: 1 }}
      >
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <View style={styles.progressPill}>
            <Text style={styles.progressText}>Step 1 of 3: Account Creation</Text>
          </View>
          <View style={styles.progressBarTrack}>
            <View style={styles.progressBarFill} />
          </View>

          <Text style={styles.title}>Create your account</Text>
          <Text style={styles.subtitle}>
            Start tracking mindful Sri Lankan meals with tailored calorie and macro targets.
          </Text>

          <TextField
            label="Full Name"
            icon="account-outline"
            placeholder="e.g. Kavindi Perera"
            value={fullName}
            onChangeText={setFullName}
          />
          <TextField
            label="Email address"
            icon="email-outline"
            placeholder="e.g. kavindi@university.ac.lk"
            keyboardType="email-address"
            value={email}
            onChangeText={setEmail}
          />
          <TextField
            label="Password"
            icon="lock-outline"
            placeholder="Create a secure password"
            isPassword
            value={password}
            onChangeText={setPassword}
          />
          <Text style={[styles.strengthHint, { color: passwordIsStrong ? colors.primary : colors.outline }]}>
            {password.length === 0
              ? 'Use 8+ characters with a number or symbol'
              : passwordIsStrong
                ? 'Strong password'
                : 'Add a few more characters or a number/symbol'}
          </Text>

          <View style={styles.dietSection}>
            <Text style={styles.label}>Primary Dietary Focus</Text>
            <View style={styles.chipRow}>
              {DIET_OPTIONS.map((opt) => (
                <Chip
                  key={opt.key}
                  label={opt.label}
                  icon={opt.icon}
                  selected={dietFocus === opt.key}
                  onPress={() => setDietFocus(opt.key)}
                />
              ))}
            </View>
          </View>

          <View style={styles.noteCard}>
            <View style={styles.noteIcon}>
              <MaterialCommunityIcons name="spa-outline" size={18} color={colors.primary} />
            </View>
            <View style={{ flex: 1 }}>
              <Text style={styles.noteTitle}>No Calorie Guilt</Text>
              <Text style={styles.noteBody}>
                We celebrate local coconut sambols, mallum greens, and vibrant curries through
                balanced portions.
              </Text>
            </View>
          </View>

          <Pressable style={styles.termsRow} onPress={() => setAgreedToTerms((a) => !a)}>
            <View style={[styles.checkbox, agreedToTerms && styles.checkboxChecked]}>
              {agreedToTerms && <MaterialCommunityIcons name="check" size={14} color={colors.onPrimary} />}
            </View>
            <Text style={styles.termsText}>
              I agree to NutriPath's Terms of Service and Privacy Policy.
            </Text>
          </Pressable>

          <View style={{ gap: spacing.sm, marginTop: spacing.sm }}>
            <Button label="Create Account & Continue" onPress={handleSubmit} />
            <View style={styles.loginRow}>
              <Text style={styles.loginText}>Already have an account? </Text>
              <Pressable onPress={onGoToLogin}>
                <Text style={styles.loginLink}>Log In</Text>
              </Pressable>
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.margin,
    height: 56,
  },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  content: { paddingHorizontal: spacing.margin, paddingBottom: spacing.lg, gap: spacing.sm },
  progressPill: {
    alignSelf: 'flex-start',
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 12,
    paddingVertical: 4,
    borderRadius: radii.pill,
  },
  progressText: { ...typography.labelMd, color: colors.primary },
  progressBarTrack: { height: 6, backgroundColor: colors.surfaceContainer, borderRadius: 3 },
  progressBarFill: { width: '33%', height: 6, backgroundColor: colors.primary, borderRadius: 3 },
  title: { ...typography.headlineMd, color: colors.onSurface, marginTop: spacing.xs },
  subtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, marginBottom: spacing.xs },
  strengthHint: { ...typography.labelSm, marginTop: -4 },
  label: { ...typography.labelMd, color: colors.onSurface },
  dietSection: { gap: spacing.xs, marginTop: spacing.xs },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  noteCard: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: colors.surfaceContainerLow,
    padding: 14,
    borderRadius: radii.md,
    alignItems: 'flex-start',
  },
  noteIcon: {
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: colors.secondaryFixed,
    alignItems: 'center',
    justifyContent: 'center',
  },
  noteTitle: { ...typography.labelMd, color: colors.primary },
  noteBody: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  termsRow: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm, marginTop: spacing.xs },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 6,
    backgroundColor: colors.surfaceContainerLowest,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 2,
  },
  checkboxChecked: { backgroundColor: colors.primary },
  termsText: { ...typography.bodySm, color: colors.onSurfaceVariant, flex: 1 },
  loginRow: { flexDirection: 'row', justifyContent: 'center' },
  loginText: { ...typography.bodyMd, color: colors.onSurfaceVariant },
  loginLink: { ...typography.labelLg, color: colors.primary },
});